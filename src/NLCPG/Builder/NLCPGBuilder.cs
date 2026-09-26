using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NL.Concurrency;
using NLCPG.Builder.Preallocation;
using NLCPG.Builder.Passes;
using NLCPG.Builder.Streaming;
using NLCPG.Builder.Concurrency;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NLCPG.Builder;

/// 从单个源码文件构建最小 Roslyn 风格代码属性图。
public sealed partial class NLCPGBuilder
{
    private static readonly IReadOnlyList<INLCPGPass> LegacyPipeline = new INLCPGPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
        OperationPass.Instance,
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };
    private static readonly IReadOnlyList<INLCPGPass> PartitionedPreOperationPipeline = new INLCPGPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
    };
    private static readonly IReadOnlyList<INLCPGPass> PartitionedPostOperationPipeline = new INLCPGPass[]
    {
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };

    private readonly Dictionary<SyntaxNode, NLCPGNode> _syntaxNodes = new(ReferenceEqualityComparer.Instance);
    private readonly GraphScopedCache<string, NLCPGNode> _symbolNodes = new(StringComparer.Ordinal);
    private readonly GraphScopedCache<string, NLCPGNode> _typeDeclNodes = new(StringComparer.Ordinal);
    private readonly GraphScopedCache<string, NLCPGNode> _methodNodes = new(StringComparer.Ordinal);
    private readonly GraphScopedCache<string, NLCPGNode> _methodParameterNodes = new(StringComparer.Ordinal);
    private readonly GraphScopedCache<string, NLCPGNode> _methodReturnNodes = new(StringComparer.Ordinal);
    private readonly GraphScopedCache<string, NLCPGNode> _methodEntryNodes = new(StringComparer.Ordinal);
    private readonly GraphScopedCache<string, NLCPGNode> _methodExitNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<NLCPGNode, string> _symbolKeysByNode = new();
    private readonly Dictionary<NLCPGNode, string> _methodOwnerSymbolKeysByBoundaryNode = new();
    private readonly Dictionary<NLCPGNode, int> _methodParameterOrdinalsByNode = new();
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByNameAndSignature = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<INamedTypeSymbol>> _baseTypeCache =
      new(StringComparer.Ordinal);
    private readonly Dictionary<NLCPGNode, HashSet<NLCPGNode>> _cfgPredecessorsByNode = new();
    private readonly Dictionary<NLCPGNode, HashSet<NLCPGNode>> _cfgSuccessorsByNode = new();
    // ⚠ 原为单个 `NLCPGGraph?`。多文件构建下每文件一张独立图，用单数会**静默**让
    //   除一张图外的全部图不再缓存 CFG 邻接（见 AddControlFlowEdge 的 ReferenceEquals 判定），
    //   也会让 worker 计算窗口只对一张图生效（分层守卫被绕过）。
    //   改用引用身份的集合：单图构建时恒含 0/1 个元素，行为与改造前逐字一致。
    private readonly HashSet<NLCPGGraph> _activeBuildGraphs =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IInvocationOperation, NLCPGNode> _callSiteNodesByInvocation =
      new(ReferenceEqualityComparer.Instance);
    // ⚠ 这三个缓存**必须按图分键**——见 GraphScopedCache 的类型注释与 GetOrCreateOperationNode 的说明。
    //   用 ReferenceEqualityComparer 分键，因为「同一张图」只可能是同一实例。
    private readonly GraphScopedCache<IOperation, NLCPGNode> _operationNodesByOperation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IInvocationOperation, IReadOnlyList<IMethodSymbol>> _resolvedCallTargetsByInvocation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IReadOnlyList<IMethodSymbol>> _resolvedCallTargetsByDispatchShape =
      new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _propertyAccessorCallSiteNodesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<SyntaxNode> _pendingOperationSyntaxTypeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, SyntaxSemanticFacts> _partitionedSyntaxFacts = new(ReferenceEqualityComparer.Instance);
    private readonly List<INamedTypeSymbol> _declaredTypes = new();
    private readonly List<NLCPGDataFlowMethodMetrics> _dataFlowMethodMetrics = new();
    private int _dataFlowBatchCount;
    /// <summary>
    /// G0-P **R-3**：本构建中 DataFlow 批次被**构造**的次数——只应由规划相位调用一次。
    /// <para>
    /// 仅供契约测试判定"提交步没有无视 plan 自行重算"。该计数是必要的，
    /// 因为"重算"与"消费 plan"在产物上**完全等价**（同一纯函数、同一输入），
    /// 故无法用任何行为断言区分（附录 X.5 变异②实测：该变异存活）。
    /// </para>
    /// </summary>
    private int _dataFlowPlanAssemblyCount;
    private int _dataFlowWorkerCount;
    private int _callGraphBatchCount;
    private bool _interproceduralBarrierCompleted;
    private readonly List<CpgWorkBatchPerformanceEvent> _workBatchPerformanceEvents = new();
    private readonly Dictionary<SyntaxNode, OperationRootPlanCacheEntry> _operationRootPlansByRoot =
      new(ReferenceEqualityComparer.Instance);
    private int _operationNodeCacheHitCount;
    private int _operationNodeCacheMissCount;
    private int _operationRootCacheHitCount;
    private int _operationRootCacheMissCount;

    // 所有 builder 级缓存的唯一互斥门。
    //
    // WorkBatch worker 只负责"构造局部图 + 产出按稳定锚点描述的 fragment"，图提交统一由 reducer
    // 串行完成；但 `GetOrCreate*` 仍会被 worker 回调调用（ControlFlowPass / DominancePass 借它
    // 复用 operation→节点映射），于是这些普通 Dictionary 会被多个 worker 并发读改写，并同时与
    // reducer 写入交错。该门把这类"读—改—写"收敛为串行临界区，保住"同一 operation/符号只返回
    // 同一个节点"这一跨 pass 图一致性前提。
    //
    // 临界区内可能调用 StringInterner.Intern（自带私锁），锁序固定为 _cacheGate → interner，
    // 且 interner 从不回调 builder，故无环、无死锁。
    private readonly object _cacheGate = new();

    internal object CacheGate => _cacheGate;

    /// <summary>
    /// **D1 归并的唯一入口**：把 fragment 按来源文件路由到各自的图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 所有写图者都应经此方法，而不是直接 <c>new CpgFragmentReducer().ReduceInto(context.Graph, …)</c>——
    /// 后者会把跨文件批次的事实全部写进一张图（设计 R10：静默错误，非崩溃）。
    /// </para>
    /// <para>
    /// 无注册表（单文件）时走原有单图重载，**逐字不变**。
    /// </para>
    /// </remarks>
    internal void ReduceFragments(
      NLCPGBuildContext context,
      IEnumerable<LocalCpgFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(fragments);

        var reducer = new CpgFragmentReducer();
        if (context.GraphRegistry is { } registry)
        {
            _ = reducer.ReduceInto(registry, fragments);
            return;
        }

        _ = reducer.ReduceInto(context.Graph, fragments);
    }

    /// <summary>
    /// 对**全部**活动图进入 worker 计算窗口。多文件时逐图进入，退出时必须逐图配对。
    /// </summary>
    /// <remarks>
    /// 用快照数组遍历而非直接遍历集合：窗口进入/退出会在 worker 线程上发生，
    /// 而集合本身可能被归并窗口的写者触碰；快照同时保证「进入的那些图」
    /// 与「退出的那些图」是同一批（即便期间集合发生了变化，配对也不会错位）。
    /// </remarks>
    private void EnterWorkerComputeWindows()
    {
        foreach (var graph in _activeBuildGraphs.ToArray())
        {
            graph.EnterWorkerComputeWindow();
        }
    }

    /// <summary>对全部活动图退出 worker 计算窗口，与 <see cref="EnterWorkerComputeWindows"/> 严格配对。</summary>
    private void ExitWorkerComputeWindows()
    {
        foreach (var graph in _activeBuildGraphs.ToArray())
        {
            graph.ExitWorkerComputeWindow();
        }
    }

    // ── G0-P 附录 R.3「分层」L1 的**另一半**：不共享可变中间态 ─────────────────────
    //
    // L1 的第一半（"worker 内不写共享图"）已由 NLCPGGraph 的 worker 窗口守卫强制。
    // 第二半此前**只有注释在声称**（附录 AB.6 边界 2 已承认），而实测确实存在
    // 窗口内无门写 builder 共享缓存的点（轮次 31 审计确证）。
    //
    // 进程内**同时**处于计算窗口（worker 循环）或归并窗口（reducer 回调）的线程数。
    // 用途有三个，都是"跨线程"问题，故必须是全局计数而非线程局部：
    //   ① L1：窗口存活期间的共享态写入必须串行化（见 WriteSharedState）；
    //   ② R.4 第 3 类：发布（FreezeQueryIndex / PersistenceWrite）必须与全部窗口不重叠
    //      （见 EnsurePublicationAllowed）；
    //   ③ 可证伪性：见 SharedStateWindowOpenCount / SharedStateWindowEnterCount。
    //
    // ⚠ 为什么**不**用 ThreadLocal<int>（曾这样写，是错的）：归并回调是 async，
    //   进入窗口的线程与退出窗口的线程可能不是同一个（await 后续可能切线程池线程）。
    //   线程局部计数会在 A 线程 +1、B 线程 -1：A 线程被永久污染（此后每次写入都白取锁），
    //   而全局计数因 B 线程读到 0 而**提前返回、永不递减**——发布守卫随即永久抛错。
    //   全局计数没有"进出的线程必须相同"这个前置条件，因此对 async 路径天然正确。
    private int _activeWorkerWindowCount;

    /// <summary>
    /// 累计**进入**过共享态窗口的次数（只增，供测试观测）。
    /// <para>
    /// <b>为什么必须存在（轮次 31 变异实测）：</b>L1 第二半的机制按设计**不改变任何行为**
    /// （它只在"窗口期写共享状态"这一本不该发生的情况下才多取一次锁；今天没有活跃竞争）。
    /// 因此变异"把 <c>sharedStateWindowEnter/Exit</c> 接线拔掉"后，
    /// <b>全部 11 条专项用例 + 全量 658 条 Cpg 用例仍然全绿</b>——即该机制在**行为层不可证伪**。
    /// 这正是附录 X.5 / Y.4 / AA / AC.5 反复得出的结论：
    /// <b>"从未记录"与"没有机制"在行为层完全等价</b>，此时判据**必须**落到结构/计数层。
    /// </para>
    /// <para>
    /// 判据示例：一次真实构建后本计数必须 <c>&gt; 0</c>（证明确实开了窗口）；
    /// 把它与"归并回调确实执行过"配对，即可区分"窗口真的开了"与"窗口是死代码"。
    /// </para>
    /// </summary>
    private long _sharedStateWindowEnterCount;

    /// <summary>
    /// 当前存活的共享态窗口数（跨线程聚合）。发布守卫的非重叠前提读它。
    /// </summary>
    internal int SharedStateWindowOpenCount => Volatile.Read(ref _activeWorkerWindowCount);

    /// <summary>
    /// 累计进入共享态窗口的次数（只增观测面）。见 <see cref="_sharedStateWindowEnterCount"/>。
    /// </summary>
    internal long SharedStateWindowEnterCount => Interlocked.Read(ref _sharedStateWindowEnterCount);

    /// <summary>
    /// 本构建是否已通过 <see cref="EnsureStageDependencyOrder"/> 的顺序校验。
    /// 发布守卫要求它为 <c>true</c>——否则"发布"可能发生在一次**从未校验过**的执行之后。
    /// </summary>
    private bool _stageDependencyOrderVerified;

    /// <summary>
    /// G0-P **R.4 第 3 类**：发布阶段的**实际执行顺序**（只增不改）。
    /// 与 <c>_executedStageOrder</c> 分开记账，因为 <c>StageDependencyTable.Stage</c>
    /// 枚举里**没有** FreezeQueryIndex / PersistenceWrite（它们不是 work-batch 阶段），
    /// 混进同一张表会破坏权威表的自洽性。
    /// </summary>
    private readonly List<string> _publicationStageOrder = new();

    /// <summary>
    /// 进入计算窗口（worker 循环）或归并窗口（reducer 回调）。可重入：每次进入都必须有一次退出。
    /// </summary>
    internal void EnterSharedStateWindow()
    {
        Interlocked.Increment(ref _activeWorkerWindowCount);
        Interlocked.Increment(ref _sharedStateWindowEnterCount);
    }

    /// <summary>
    /// 退出计算/归并窗口。与 <see cref="EnterSharedStateWindow"/> 必须严格配对
    /// （执行器用 <c>try/finally</c> 闭合，抛异常时也会退出）。
    /// <para>
    /// 计数**饱和**在 0：若因某条异常路径多减一次，宁可使计数停在 0（发布守卫随后放行一次
    /// 本应被拒的发布），也不能让它变负——负值会让 <see cref="EnsurePublicationAllowed"/>
    /// 的 <c>!= 0</c> 判据**永久**为真，把此后每次构建的发布全部打死。
    /// 这是"守卫自身故障时选哪一边失败"的取舍：偏向可用性，同时不影响正常路径的正确性。
    /// </para>
    /// </summary>
    internal void ExitSharedStateWindow()
    {
        if (Interlocked.Decrement(ref _activeWorkerWindowCount) < 0)
        {
            Interlocked.Exchange(ref _activeWorkerWindowCount, 0);
        }
    }

    /// <summary>
    /// G0-P **R.3「分层」L1（第二半）**：在计算/归并窗口**存活期间**对 builder 级
    /// 共享可变中间态的**唯一合法写入口**。按窗口状态决定是否取门，调用者无需判断取门时机。
    /// <para>
    /// <b>为什么需要（轮次 31 审计）：</b>L1 断言"worker 只算 fragment"，此前只有
    /// "worker 不写共享图"这半被 <c>NLCPGGraph</c> 守卫强制；"不共享可变中间态"这半
    /// 仅由注释声称（附录 AB.6 边界 2 已自承）。本次审计枚举了 builder 具名容器的全部
    /// 写点，确认这些写入**确实**发生在窗口存活期间（归并线程上），且此前**没有任何机制**
    /// 保证其串行性。
    /// </para>
    /// <para>
    /// <b>⚠ 如实定性：这是"消除未写下的巧合"，不是"修复今天的活跃竞争"。</b>
    /// 今天的串行性来自两条从未写下的事实：① 结果通道是 <c>SingleReader</c>
    /// （归并回调只有一条，故这些写彼此串行）；② 这些容器的读者都排在串行相位。
    /// 因此本方法<b>不改变当前可观测行为</b>。它的价值在于：L1 的不变式不再寄托于
    /// 那两条巧合——任一条将来变化，失效形态都是**静默**的（丢失更新、枚举中途变形），
    /// 与轮次 29 实测"3 个并发 <c>AddNode</c> 后只剩 2 个节点且无异常"同源。
    /// 把它写成"修了一个并发 bug"会夸大证据，故此处按机制现状记录。
    /// </para>
    /// <para>
    /// <b>为什么不是"窗口内禁止写入"：</b>归并层（L2）**必须**写 builder 级记账（调用点映射、
    /// 数据流指标等），否则这些结果无处累积。可行的不变量只能是"窗口存活期间的写入必须经门串行化"。
    /// </para>
    /// <para>
    /// <b>为什么"只在窗口存活时取门"：</b>窗口外（串行相位）本就无并发，取门是纯开销；
    /// 且既有串行路径（如 <c>SyntaxPass</c> 填 <c>_declaredTypes</c>、<c>MethodDecorationPass</c>
    /// 填方法边界缓存）**已经**各自持门，语义不变。
    /// </para>
    /// <para>
    /// <b>判据为什么读全局计数、而不读"本线程是否在窗口内"：</b>本方法关心的是
    /// "此刻**是否存在**窗口，可能与我的写入重叠"，那是全局性质。串行相位下计数恒为 0，
    /// 故不会把无并发的串行写入误判为需要取门。
    /// </para>
    /// <para>
    /// <b>为什么采用"唯一入口"而不是"写点自查守卫"（设计取舍，如实记录）：</b>
    /// 曾考虑在写点调用断言"当前是否持门"。该形态两种摆法都不成立——
    /// 摆在 <c>lock</c> **之前**，归并线程此刻必然尚未持门，合法归并会被全部误抛；
    /// 摆在 <c>lock</c> **之内**，<c>Monitor.IsEntered</c> 恒为真，断言永不触发，
    /// 于是退化为"注释式声明"——正是本附录 <c>N.4/P.4/Z.1/AA</c> 反复点名的形态。
    /// 故改为<b>结构性预防</b>：让"窗口内取门"成为不可绕过的单一入口，比事后探测更强。
    /// </para>
    /// <para>
    /// ⚠ <b>覆盖边界（勿读作已闭环）：</b>本机制强制"**经此入口**的写入必被串行化"；
    /// 它<b>不</b>具备编译期能力阻止某处绕过本入口直接对私有字段赋值
    /// （通行做法是 partial 类内直接写字段）。故仍需审阅纪律：窗口期间新增的共享写点必须走本方法。
    /// 另：<c>NLCPGBuildContext</c> 的 <c>_operationInventory</c> 等集合由**归并回调**写入，
    /// 目前同样依赖"单归并者"这一未强制的巧合，尚未纳入本入口。图侧不写共享图由
    /// <c>NLCPGGraph</c> worker 窗口守卫独立强制，二者互补。
    /// </para>
    /// <para>
    /// <b>为什么没有"成员名"参数：</b>本方法不抛异常、只负责串行化，故没有异常消息要拼；
    /// 传一个仅供"留证"的名字会是**未被读取的参数**——它看上去在产出诊断，实则不产出，
    /// 属于本附录点名的"声明代替机制"。写入点身份由调用点自身的表达式直接表达。
    /// </para>
    /// </summary>
    /// <param name="mutation">实际的写入动作。须保持短小、不自回调 builder 公开入口。</param>
    private void WriteSharedState(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        if (Volatile.Read(ref _activeWorkerWindowCount) <= 0)
        {
            // 串行相位：无并发，保持既有行为与零额外开销。
            mutation();
            return;
        }

        // 窗口存活（worker 线程或归并线程）：必须串行化。
        // Monitor 可重入，故调用者已持门时再取一次不会死锁。
        lock (_cacheGate)
        {
            mutation();
        }
    }

    /// <summary>
    /// G0-P **R.4 第 3 类（发布依赖）**：发布（FreezeQueryIndex / PersistenceWrite）
    /// 只能发生在**全部阶段都已归并完毕**之后，且与任何阶段执行**不重叠**。
    /// <para>
    /// <b>为什么需要（轮次 31 审计）：</b>计划文档 <c>:1836</c> 自述第 3 类"仍只有文字描述，
    /// 无任何机制"。审计把这一条拆成两半，结论是**只有一半被强制**：
    /// </para>
    /// <list type="bullet">
    /// <item>前半（冻结后不得再写图）——**已由机制强制**：<c>NLCPGGraph.EnsureMutable</c>
    /// 的 frozen 检查（"The graph is frozen and cannot be mutated."）是全部五个变更入口
    /// （AddNode / ImportMutableFacts / AddEdge / AddKnownNodeCartesianEdges / RegisterSource）
    /// 的唯一咽喉。此half不需要新机制，文档应改记为"传递性强制"。</item>
    /// <item>后半（发布与阶段执行**不重叠**、且必须晚于全部归并）——**确实无机制**。
    /// 此前它由 <c>Build</c> 里的书写顺序保证：第 611 行校验 → 第 614 行冻结 → 第 615 行释放
    /// → 第 620 行持久化。书写顺序**不是不变量**：任何人把 MeasureStage 上移几行就能让
    /// 发布与仍然存活的 worker 重叠，而现有测试**无一**能发现（审计确证无测试钉住该顺序）。
    /// 本方法把"顺序"变成 fail-closed 前置条件。
    /// </item>
    /// </list>
    /// <para>
    /// ⚠ 这项检查**必须**在发布动作（冻结/持久化）**之前**调用，而不是之后：
    /// 冻结会把图推到只读，事后检查只能证明"已经晚了"。
    /// </para>
    /// </summary>
    /// <param name="publicationStage">正在进入的发布阶段名。</param>
    private void EnsurePublicationAllowed(string publicationStage)
    {
        var reason = EvaluatePublicationGate(
          publicationStage,
          _stageDependencyOrderVerified,
          Volatile.Read(ref _activeWorkerWindowCount));
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>
    /// G0-P R.4 第 3 类的**唯一判定函数**（纯函数：两个前提由参数传入）。
    /// <para>
    /// <b>为什么判定与抛出分开、且前提由参数传入（轮次 31 实测得出，非风格偏好）：</b>
    /// 首版把判定直接写在 <see cref="EnsurePublicationAllowed"/> 里抛出。变异验证时
    /// 把窗口计数改成恒读 0（即**让判定永不触发**），专项用例仍 **5/5 全绿** ⇒
    /// 抛出分支是**未被任何证据覆盖的死代码**。这正是附录 AC.5 自己写的规则：
    /// "『接线存在』必须与『判定会拒绝』分开测"。
    /// </para>
    /// <para>
    /// 生产路径下两个前提到达发布点时**必然满足**（否则构建早已抛出），故拒绝分支
    /// **无法**被任何行为用例触达。把前提提成参数后，用例可注入取值**直接**钉住判定，
    /// 从而让"会拒绝"成为可观测事实——这是该分支唯一的可观测接缝。
    /// </para>
    /// <para>
    /// 只保留**一处**判定实现：接线处与用例调用的是**同一个**函数，
    /// 不存在"测试测的那份与生产跑的那份各自漂移"（附录 N.4/P.4 的形态）。
    /// </para>
    /// </summary>
    /// <param name="publicationStage">正在进入的发布阶段名。</param>
    /// <param name="stageDependencyOrderVerified">顺序前提：阶段依赖校验是否已通过。</param>
    /// <param name="activeWorkerWindowCount">非重叠前提：当前存活的 worker 窗口数。</param>
    /// <returns>应拒绝时返回原因；放行时返回 <c>null</c>。</returns>
    internal static string? EvaluatePublicationGate(
      string publicationStage,
      bool stageDependencyOrderVerified,
      int activeWorkerWindowCount)
    {
        // ① 顺序前提：必须先通过阶段依赖权威表校验。否则发布的是"从未被校验过的执行"的产物。
        if (!stageDependencyOrderVerified)
        {
            return $"G0-P R.4 第 3 类（发布依赖）被违反：准备进入发布阶段 `{publicationStage}`，"
              + "但本构建尚未通过 StageDependencyTable 的阶段依赖顺序校验。"
              + "发布必须晚于【全部后置 pass 已执行且顺序已校验】——"
              + "否则冻结的是一个可能已残缺的图，且该残缺再也不会被检出。";
        }

        // ② 非重叠前提：任何 worker 计算窗口仍存活 ⇒ 阶段执行与发布重叠。
        //    读的是跨线程计数（不是本线程 ThreadLocal）——发布线程自己必然不在窗口内，
        //    因此"只看本线程"会给出恒定通过的空洞判定。
        if (activeWorkerWindowCount != 0)
        {
            return $"G0-P R.4 第 3 类（发布依赖）被违反：准备进入发布阶段 `{publicationStage}` 时，"
              + $"仍有 {activeWorkerWindowCount} 个 worker 计算窗口存活。"
              + "发布（FreezeQueryIndex / PersistenceWrite）必须与全部阶段执行【不重叠】；"
              + "重叠会让发布读到尚未归并完的中间态，并把该中间态固化成查询态。"
              + "修法：发布前 `await` 全部 worker 结束（执行器已在退出前 Task.WhenAll），"
              + "或把该发布动作移回 Build 的串行尾段。";
        }

        return null;
    }

    /// <summary>
    /// 记录一次发布阶段的进入（顺序留证）。与 <see cref="EnsurePublicationAllowed"/> 配对：
    /// 先校验、后留证，故留证里**绝不会**出现被拒绝的发布。
    /// </summary>
    private void RecordPublicationStage(string publicationStage)
    {
        _publicationStageOrder.Add(publicationStage);
    }

    /// <summary>
    /// 最近一次构建的**发布阶段实际执行顺序**（只读观测面）。
    /// <para>
    /// <b>为什么必须暴露它（轮次 31 自查）：</b>首版只写了私有字段、**没有任何读者**——
    /// 于是"留证"这一说法在观测上**完全不存在**："从未记录"与"没有机制"行为等价
    /// （附录 X.5/Y.4/AA 的教训）。这与 <c>LastExecutedStageOrder</c> 同类：
    /// 两者的价值都在**可被证伪**，而非被内部使用。
    /// </para>
    /// <para>
    /// 判据应落在**序列层**：例如断言"发布顺序恰为 Freeze → Persistence"、
    /// "启用持久化时两项都在、未启用时只有 Freeze"。
    /// </para>
    /// </summary>
    internal IReadOnlyList<string> LastPublicationStageOrder => _publicationStageOrder;

    private readonly Dictionary<string, long> _passElapsedMilliseconds = new(StringComparer.Ordinal);
    private readonly NLCPGBuilderOptions _options;
    private readonly IConcurrencyPool _concurrencyPool;
    private readonly CpgWorkBatchBuilder _workBatchBuilder;
    private readonly CpgWorkBatchExecutor _workBatchExecutor;

    /// <summary>
    /// 本次构建的**中立分片计划**（S5-2）；未规划时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由 <see cref="_options"/> 的 <c>WorkShardPlanner</c> 端口在
    /// <see cref="BuildManyDocuments"/> 里现算，**不进 options**——因为算它需要
    /// 「正在用来构图的这个 builder」，把结果塞回 options 会把这次调用变成一次性配置。
    /// </para>
    /// <para>
    /// <c>null</c> 即「本次构建未规划分片」——改造前**全部**构建的形态。
    /// 此时 <see cref="ResolveShardOrder"/> 走回退带，单文档逐字返回局部序号，
    /// 故既有单文件路径行为不变。
    /// </para>
    /// <para>
    /// <b>非 readonly</b>：每次构建重算（builder 可被复用；上一次的计划不得泄漏到下一次）。
    /// </para>
    /// </remarks>
    private CpgWorkShardPlan? _workShardPlan;

    /// <summary>多文档构建中「文件 → 文档序号」的查找表；单文档时为 <c>null</c>。</summary>
    /// <remarks>
    /// 供 <see cref="ResolveShardOrder"/> 计算回退带序号（需要文档序号与总数）。
    /// 单文档时 <c>null</c> ⇒ 走「直接返回局部序号」的退化分支。
    /// 每次 <c>Build</c> 入口按当次 context 重算——builder 可被复用多次构建。
    /// </remarks>
    private IReadOnlyDictionary<string, int>? _documentOrdinalsByFilePath;

    private int _documentCountForShardOrder = 1;

    /// <summary>
    /// **方法序号普查**（S5-2 adapter 的输入来源）：给出某语法树里**每个操作根**的
    /// （方法序号, 成本）对，序号与成本口径**与构图内部逐字同源**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么必须由 NLCPG 提供，而不是让调用方自己扫语法树：</b>分片计划的
    /// <c>MethodOrders</c> 最终要匹配 <see cref="CpgWorkItem.StableOrder"/>，而后者就是
    /// <see cref="GetOperationRootPlans"/> 分配的 <c>order</c>。若调用方按自己的规则重数一遍
    /// （例如只看 <c>MethodDeclarationSyntax</c>），一旦与实际枚举顺序不一致，
    /// 计划里的方法序号就会指向**别的方法**——分片边界静默错位，且不抛异常。
    /// 故序号必须来自**同一个**枚举函数，本方法即是它的公开投影。
    /// </para>
    /// <para>
    /// 成本口径同样必须同源：取的是 <see cref="CpgWorkBatchCostModel.Estimate"/> 在
    /// <c>EffectiveWorkBatchCostOptions</c> 下算出的 <c>Cost</c>（方法体行数），
    /// 而**不是**调用方自行数的行数。
    /// </para>
    /// <para>
    /// 本方法的枚举顺序与顺序语义<b>逐字等于</b>计划器所依赖的
    /// 「方法 → 属性访问器 → 全局语句」，三段的先后不可调换。
    /// </para>
    /// </remarks>
    /// <param name="semanticModel">该文件的语义模型。</param>
    /// <param name="root">该文件的语法根。</param>
    /// <returns>按方法序号升序的（序号, 成本）列表；无操作根时为空。</returns>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    public IReadOnlyList<(int MethodOrder, int EstimatedCost)> DescribeMethodOrders(
      SemanticModel semanticModel,
      SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(root);

        var plans = GetOperationRootPlans(root, semanticModel);
        if (plans.Count == 0)
        {
            return Array.Empty<(int, int)>();
        }

        var described = new List<(int MethodOrder, int EstimatedCost)>(plans.Count);
        foreach (var plan in plans)
        {
            var lineSpan = semanticModel.SyntaxTree.GetLineSpan(plan.BodySyntax.Span);
            var estimate = CpgWorkBatchCostModel.Estimate(
              lineSpan.StartLinePosition.Line,
              lineSpan.EndLinePosition.Line,
              _options.EffectiveWorkBatchCostOptions);
            described.Add((plan.Order, estimate.Cost));
        }

        return described;
    }

    /// <summary>
    /// **装箱的唯一入口**（S5-2）：在「工作项 → 批次」这一条边界上套用分片计划。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么注入在这里，而不是每个 <see cref="CpgWorkItem"/> 的构造点：</b>
    /// 全仓有 5 个工作项构造点（且会随改造增减），但**全部**都要经过
    /// <c>_workBatchBuilder.Build</c> 才成为批次。把序号解析放在这条边界上，
    /// 就只需维护一处，且新加构造点**自动**被覆盖——否则每个新构造点都是一次
    /// 「忘记赋全局序号 ⇒ 跨文件碰撞 ⇒ 执行器抛异常」的机会。
    /// </para>
    /// <para>
    /// 无计划且单文档时 <see cref="ApplyWorkShardPlan"/> 原样返回输入序列，
    /// 即**不做任何复制**，故改造前的单文件路径逐字不变（含身份与顺序）。
    /// </para>
    /// </remarks>
    private IReadOnlyList<CpgWorkBatch> BuildWorkBatches(
      string sourceFilePath,
      IReadOnlyList<CpgWorkItem> workItems)
    {
        return _workBatchBuilder.Build(sourceFilePath, ApplyWorkShardPlan(workItems));
    }

    /// <summary>
    /// 按分片计划**改写**各工作项的 <see cref="CpgWorkItem.ShardOrder"/>；
    /// 无需改写时原样返回（不分配新序列）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 改写而非「在构造点直接算」还有一个好处：<see cref="CpgWorkItem.StableOrder"/>
    /// 与 <see cref="CpgWorkItem.SourceFilePath"/> 等**载荷查找键保持原值**，
    /// 故 <c>DataFlowPass</c> 的 <c>methodPartitions[item.StableOrder]</c> 下标语义不受影响
    /// （这是 §1.6 的硬约束：<c>StableOrder</c> 不得改为全局序号）。
    /// </para>
    /// <para>
    /// 单文档且无计划 ⇒ <see cref="ResolveShardOrder"/> 恒返回
    /// <c>item.StableOrder</c> ⇒ <c>needsRewrite</c> 全程为 <c>false</c> ⇒ 返回原序列。
    /// </para>
    /// </remarks>
    private IEnumerable<CpgWorkItem> ApplyWorkShardPlan(IReadOnlyList<CpgWorkItem> workItems)
    {
        if (_workShardPlan is null && _documentOrdinalsByFilePath is null)
        {
            // 单文件 + 无计划：改造前的全部形态，零开销零分配。
            return workItems;
        }

        var rewritten = new List<CpgWorkItem>(workItems.Count);
        foreach (var item in workItems)
        {
            var shardOrder = ResolveShardOrder(item.SourceFilePath, item.StableOrder);
            if (shardOrder == item.ShardOrder)
            {
                rewritten.Add(item);
                continue;
            }

            rewritten.Add(new CpgWorkItem(
              item.StableOrder,
              item.SourceFilePath,
              item.MethodSymbolKey,
              item.SpanStart,
              item.SpanEnd,
              item.EstimatedCost,
              item.Kind,
              shardOrder));
        }

        return rewritten;
    }

    /// <summary>
    /// **唯一的 <see cref="CpgWorkItem.ShardOrder"/> 解析点**（S5-2）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 所有工作项构造点都必须经过本方法，而**不得**各自决定序号——序号唯一性是执行器的
    /// 硬契约（<c>CpgWorkBatchExecutor.cs:578</c> 抛
    /// <c>"WorkBatch shard orders must be unique."</c>），分散决定必然产生碰撞。
    /// </para>
    /// <para>
    /// 解析优先级：<b>计划命中 → 计划带序号</b>；未命中 → <b>回退带序号</b>。
    /// 两带不相交，故无论计划覆盖多少项，结果都唯一（见 <see cref="CpgWorkShardPlan"/>）。
    /// </para>
    /// </remarks>
    /// <param name="sourceFilePath">该工作项所属文件。</param>
    /// <param name="localOrder">该文件内局部序号（即 <see cref="CpgWorkItem.StableOrder"/>）。</param>
    /// <returns>全局唯一的调度序号。</returns>
    private int ResolveShardOrder(string sourceFilePath, int localOrder)
    {
        if (_workShardPlan is not null &&
            _workShardPlan.TryResolveItemOrder(sourceFilePath, localOrder, out var planned))
        {
            return planned;
        }

        // 单文档（或无文档集）时 documentOrdinals 为 null ⇒ 退化为 localOrder，
        // 与改造前逐字一致（含无计划时 ShardOrder == StableOrder 这一既有事实）。
        if (_documentOrdinalsByFilePath is null ||
            !_documentOrdinalsByFilePath.TryGetValue(sourceFilePath, out var documentIndex))
        {
            return localOrder;
        }

        // 多文档：按文档序号错开。计划为 null 时用空计划，其 PlannedItemCount == 0，
        // 故回退带从 0 开始且各文件区间仍互不重叠。
        return (_workShardPlan ?? CpgWorkShardPlan.Empty).ComputeFallbackShardOrder(
          documentIndex,
          _documentCountForShardOrder,
          localOrder);
    }

    /// <summary>
    /// 调用应用层端口，得到本次构建的分片计划（中立契约）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 未配置端口（<c>_options.WorkShardPlanner is null</c>）时返回 <c>null</c>——
    /// 这是改造前**全部**构建的形态，也是唯一不引入任何新行为的路径。
    /// </para>
    /// <para>
    /// 端口返回 <c>null</c> 或空序列时同样返回 <c>null</c>（视作「本批不规划」），
    /// 而不是空计划——空计划与 <c>null</c> 在 <see cref="ResolveShardOrder"/> 里都走回退带，
    /// 但 <c>null</c> 能让「未规划」这一事实在调试时一眼可辨。
    /// </para>
    /// </remarks>
    private CpgWorkShardPlan? BuildWorkShardPlan(
      NLCPGBuildContext context,
      NLCPGDocumentSet documentSet)
    {
        if (_options.WorkShardPlanner is not { } planner)
        {
            return null;
        }

        // 端口按**文件登记序**取文档，与 documentSet.FilePaths 同一顺序，
        // 使 BuildManyDocuments 与 Build 看到的批次顺序一致（确定性）。
        var documents = new NLCPGBuildDocument[documentSet.Count];
        for (var index = 0; index < documentSet.Count; index += 1)
        {
            var filePath = documentSet.FilePaths[index];
            var documentContext = documentSet.ResolveContext(filePath);
            documents[index] = new NLCPGBuildDocument(
              filePath,
              documentContext.Source,
              documentContext.SemanticModel,
              documentContext.Root);
        }

        var assignments = planner.CreateShardAssignments(this, documents);
        return assignments is null || assignments.Count == 0
          ? null
          : new CpgWorkShardPlan(assignments);
    }

    internal bool PartitionPerformanceDiagnosticsEnabled =>
      _options.PerformanceDiagnostics == NLCPGPerformanceDiagnosticsMode.Diagnostic &&
      _options.PartitionPerformanceEventSink is not null;

    internal int PartitionPerformanceMaxDegreeOfParallelism =>
      _options.EffectiveMaxDegreeOfParallelism;

    internal string? PartitionPerformanceRunId => _options.PerformanceRunId;

    public NLCPGFlowSummaryMetrics LastFlowSummaryMetrics { get; private set; } = NLCPGFlowSummaryMetrics.Empty;

    // M1（跨过程计划缩窄）容量账本：最近一次 build 的【保留容量】读数。
    // 与 LastFlowSummaryMetrics 一样是只读结果，不是配置开关；不参与任何正确性判据。
    internal InterproceduralPlanCapacityLedger LastInterproceduralPlanCapacity { get; private set; } =
      InterproceduralPlanCapacityLedger.Empty;

    public NLCPGBuildMetrics LastBuildMetrics { get; private set; } = NLCPGBuildMetrics.Empty;

    private sealed record CapabilityBuildPlan(
        NLCPGCapability ResolvedCapabilities,
        bool EmitSyntaxTokens,
        bool EmitReferences,
        bool EmitTypeReferences,
        bool RequiresMethodModel,
        bool RequiresCallTargets,
        bool RequiresCfg,
        bool RequiresDataFlow,
        bool RequiresInterproceduralDataFlow,
        bool RequiresDominance,
        bool RequiresControlDependence);

    private sealed record LoopControlTargets(IOperation? ContinueTarget, IOperation? BreakTarget);
    // internal（而非 private）：SparseSetStore 提升为 internal 后，其
    // ApplyDefinitionTransfer 签名不能再引用可访问性更低的参数类型（CS0051）。
    // 仍是非公开类型，不构成公开 API；NLCPGNodeIdContractTests 只要求"非 public"。
    internal readonly record struct DefinitionFact(string LocationKey, string? BaseKey, string Category, string? PathKey = null);

    // 跨过程计划发布窗口的双上界（原为方法内常量，因容量治理需在同一处引用而提升到类级）。
    // 二者共同封顶一个窗口同时存活的计划行数，避免把百万级行同时留在堆上。
    private const int WindowMaxGroupsPerPublish = 64;

    private const int WindowMaxRowsPerPublish = 131_072;
    private sealed record DataFlowOperationIndex(
        IReadOnlyDictionary<IOperation, NLCPGNode> NodesByOperation);

    // 以给定选项初始化构图器；未提供时使用默认配置。
    public NLCPGBuilder(NLCPGBuilderOptions? options = null, IConcurrencyPool? concurrencyPool = null)
    {
        _options = options ?? NLCPGBuilderOptions.CreateDefault();
        _concurrencyPool = concurrencyPool ?? new BoundedConcurrencyPool();
        _workBatchBuilder = new CpgWorkBatchBuilder(
          _options.EffectiveWorkBatchCostOptions,
          _options.EffectiveWorkBatchMaxMethodsPerBatch,
          _options.EffectiveWorkBatchMaxEstimatedBytesPerBatch);
        _workBatchExecutor = new CpgWorkBatchExecutor(
          new CpgWorkBatchExecutorOptions(
            _options.EffectiveMaxDegreeOfParallelism,
            telemetrySink: RecordWorkBatchPerformanceEvent,
            performanceRunId: _options.PerformanceRunId,
            useSynchronousExecution: _options.UseSynchronousLocalWorkBatchExecution),
          _concurrencyPool,
          // G0-P 附录 R.3「分层」L1：worker 只算 fragment，不写共享图。
          // 钩子读【当前构建】的图（_activeBuildGraph 在 Build() 入口赋值），
          // 而非构造期捕获某个固定实例——builder 可被复用多次构建，图每次都是新的。
          // 目标为 null（构建已收尾/未在构建中）时不做任何判定，保持 fail-open：
          // 该情形下没有"共享图"这一概念可言，硬判会把收尾路径误报成分层违规。
          workerComputeWindowEnter: () => EnterWorkerComputeWindows(),
          workerComputeWindowExit: () => ExitWorkerComputeWindows(),
          // G0-P 附录 R.3「分层」L1 的**另一半**：不共享可变中间态。
          // 与上面两个钩子同一时机、同一线程，但管的是 builder 侧容器而非图侧：
          // 窗口存活期间对 builder 级共享可变中间态的**写入**必须串行化（见 WriteSharedState）。
          // 注意是"写"而非"读"——这些容器在窗口期间的读是安全的（写者都排在窗口之外），
          // 把守卫挂到读侧会把合法读全部误报，详见该方法的注释。
          sharedStateWindowEnter: EnterSharedStateWindow,
          sharedStateWindowExit: ExitSharedStateWindow,
          // G0-P R-4：执行期观测 → 与【声明表】对账（fail-closed）。
          // 此前 BatchPlanCapableStages 只是声明，无机制校验它与真实执行一致；
          // 漏写的阶段会静默归入 NoBatchPlan 而豁免整套 R-4 治理（附录 AA）。
          stageExecutionObserved: ReconcileObservedStageExecution);
    }

    // 从源码文本直接创建语义模型并构建冻结后的 CPG。
    public NLCPGGraph BuildFromSource(string source, string filePath = "input.cs")
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(NLCPGBuildContext.CreateFromSource(source, filePath));
        }

        var sourceInput = NLCPGBuildContext.CreateSourceSemanticInput(source, filePath);
        var identityFactory = new StableNodeIdentityFactory();
        var preflightStopwatch = Stopwatch.StartNew();
        var preflightBuilder = new NLCPGBuilder(CreateAnchorDiscoveryOptions());
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(NLCPGBuildContext.CreateAnchorDiscovery(
            sourceInput.SemanticModel,
            sourceInput.Root,
            source,
            filePath,
            identityFactory,
            collector.Add));
        preflightStopwatch.Stop();

        var allocation = collector.CreateAllocation();
        var graph = Build(NLCPGBuildContext.Create(
          sourceInput.SemanticModel,
          sourceInput.Root,
          source,
          filePath,
          allocation,
          identityFactory));
        LastBuildMetrics = LastBuildMetrics with
        {
            AnchorDiscoveryAnchorCount = collector.Count,
            AnchorDiscoveryElapsedMilliseconds = preflightStopwatch.ElapsedMilliseconds,
            AnchorDiscoveryPassElapsedMilliseconds = CopyStageElapsedMilliseconds(
              preflightBuilder.LastBuildMetrics.PassElapsedMilliseconds),
            BuildInventoryMetrics = LastBuildMetrics.BuildInventoryMetrics! with
            {
                PreallocatedAnchorDiff = CpgBuildInventory.CompareAnchors(
                  collector.Anchors,
                  graph.Nodes),
            },
        };
        return graph;
    }

    // 复用外部提供的语义模型与语法根来构建 CPG。
    public NLCPGGraph BuildFromSemanticModel(SemanticModel semanticModel, SyntaxNode root, string source, string filePath)
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(NLCPGBuildContext.Create(semanticModel, root, source, filePath));
        }

        var identityFactory = new StableNodeIdentityFactory();
        var preflightStopwatch = Stopwatch.StartNew();
        var preflightBuilder = new NLCPGBuilder(CreateAnchorDiscoveryOptions());
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(NLCPGBuildContext.CreateAnchorDiscovery(
            semanticModel,
            root,
            source,
            filePath,
            identityFactory,
            collector.Add));
        preflightStopwatch.Stop();

        var allocation = collector.CreateAllocation();
        var graph = Build(NLCPGBuildContext.Create(semanticModel, root, source, filePath, allocation, identityFactory));
        LastBuildMetrics = LastBuildMetrics with
        {
            AnchorDiscoveryAnchorCount = collector.Count,
            AnchorDiscoveryElapsedMilliseconds = preflightStopwatch.ElapsedMilliseconds,
            AnchorDiscoveryPassElapsedMilliseconds = CopyStageElapsedMilliseconds(
              preflightBuilder.LastBuildMetrics.PassElapsedMilliseconds),
            BuildInventoryMetrics = LastBuildMetrics.BuildInventoryMetrics! with
            {
                PreallocatedAnchorDiff = CpgBuildInventory.CompareAnchors(
                  collector.Anchors,
                  graph.Nodes),
            },
        };
        return graph;
    }

    /// <summary>
    /// **多文件构建入口（D1）**：为一组源文件各建一张独立图，并让工作批次**跨文件**装箱。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是 D1「跨文件凑标准大小」的唯一生产入口。此前仓库里不存在任何多文件入口
    /// （全 <c>src/</c> 检索 <c>BuildFromSources</c>/<c>BuildMany</c>/<c>GraphByFile</c> = 0 命中），
    /// 故 T4 的全部按项路由逻辑在此之前**不可达**。
    /// </para>
    /// <para>
    /// <b>为什么每文件一张独立图：</b>权威设计 <c>2026-09-24-unified-work-scheduler-design.md:923</c>
    /// 明确「每文件一张独立图」，并在 <c>:955</c> 警告 §4.2 的「合并为同一张图」指的是
    /// <b>F 的图（每文件一张）</b>，不是一张全局主图。故返回的是**每文件一张**的字典。
    /// </para>
    /// <para>
    /// <b>共享字符串表：</b>全部兄弟图共用同一个 <see cref="StringInterner"/> 与
    /// <see cref="StableNodeIdentityFactory"/>。这是硬性要求而非优化——
    /// <see cref="NLCPGGraph"/> 默认各自 <c>new StringInterner()</c>（<c>NLCPGGraph.cs:154</c>），
    /// 而 <see cref="StableNodeAnchor"/> 以 <c>FilePathId</c>（字符串表内的整数）参与相等性，
    /// 故各持一份会让同名文件在不同图里得到**不同的 <c>FilePathId</c>**，
    /// 锚点即不可跨图比较，fragment 也就无法被安全路由。
    /// </para>
    /// <para>
    /// <b>单文件输入：</b>请直接用 <see cref="BuildFromSource"/>；本方法要求
    /// <paramref name="files"/> 非空。单元素输入同样可用，且其结果与
    /// <see cref="BuildFromSource"/> 的图**逐字一致**（每文件一张图，只有一张）。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">当 <paramref name="files"/> 为空或含重复路径时。</exception>
    /// <exception cref="NotSupportedException">当启用了持久化/流式分片时（见 <c>Build</c> 内的说明）。</exception>
    public IReadOnlyDictionary<string, NLCPGGraph> BuildMany(
      IReadOnlyList<(string FilePath, string Source)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var documents = new NLCPGBuildDocument[files.Count];
        for (var index = 0; index < files.Count; index += 1)
        {
            documents[index] = new NLCPGBuildDocument(files[index].FilePath, files[index].Source);
        }

        return BuildManyDocuments(documents).Graphs;
    }

    /// <summary>
    /// **多文件构建入口（D1）**，由调用方提供已共享的语义模型。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="BuildMany(IReadOnlyList{ValueTuple{string, string}})"/> 的唯一区别是
    /// <see cref="NLCPGBuildDocument.SemanticModel"/>/<see cref="NLCPGBuildDocument.Root"/>：
    /// 调用方若**已经**为整批文件建好一份共享 compilation（目录分析路径即是如此），
    /// 就应把语义模型传进来，而不是让构建器再解析一遍——
    /// 否则规则管线手里的语义模型与图构建用的是**两个不同实例**，
    /// 而 Roslyn 的 <see cref="SemanticModel"/> 并非跨 compilation 可互换。
    /// </para>
    /// <para>
    /// 全部文档必须**统一**提供或不提供语义模型；混用会抛 <see cref="ArgumentException"/>。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// 当 <paramref name="documents"/> 为空、含重复路径、路径为空，或语义模型提供方式混用时。
    /// </exception>
    /// <exception cref="NotSupportedException">当启用了持久化/流式分片时。</exception>
    public NLCPGMultiFileBuildResult BuildManyDocuments(
      IReadOnlyList<NLCPGBuildDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(documents));
        }

        // 重复路径会让「按路径解析」失去唯一答案（注册表本身也会 fail-closed 拒绝），
        // 故在入口即用同一套 Ordinal 语义检出，错误消息指向**输入**而不是注册表内部。
        var seenFilePaths = new HashSet<string>(StringComparer.Ordinal);
        var suppliedSemanticModelCount = 0;
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentException.ThrowIfNullOrWhiteSpace(document.FilePath);
            if (!seenFilePaths.Add(document.FilePath))
            {
                throw new ArgumentException(
                  $"Duplicate source file path '{document.FilePath}'. Each file must appear exactly once.",
                  nameof(documents));
            }

            if (document.SemanticModel is not null || document.Root is not null)
            {
                if (document.SemanticModel is null || document.Root is null)
                {
                    throw new ArgumentException(
                      $"Document '{document.FilePath}' must supply both SemanticModel and Root, or neither.",
                      nameof(documents));
                }

                suppliedSemanticModelCount += 1;
            }
        }

        // 混用会产生两份互不相干的 compilation ⇒ 跨文件符号不可比 ⇒ 静默错解。
        if (suppliedSemanticModelCount != 0 && suppliedSemanticModelCount != documents.Count)
        {
            throw new ArgumentException(
              "Either all documents supply a SemanticModel/Root pair or none do. Mixing them would "
              + "assemble the batch from two unrelated compilations, making cross-file symbols incomparable.",
              nameof(documents));
        }

        // 预分配 NodeId 需要「先做一轮锚点发现，再按同一份分配表建图」。
        // 而多文件走 registry.GetOrAdd，它不接受预分配表（每张图各自持有自己的表）。
        // ⇒ fail-closed 明确拒绝，而不是塞一张空表进去产出**看似正常但 NodeId 不稳定**的图。
        if (RequiresPreallocatedNodeIds())
        {
            throw new NotSupportedException(
              "Preallocated node ids are not supported for multi-file builds, because each sibling graph "
              + "owns its own deterministic node-id allocation. Build each file separately when "
              + "UsePreallocatedNodeIds or streaming persistence is required.");
        }

        // ① 全部兄弟图共享一份字符串表与身份工厂（见上方说明：这是正确性前提，不是优化）。
        var identityFactory = new StableNodeIdentityFactory();
        var stringInterner = new StringInterner();
        var registry = new NLCPGGraphRegistry(identityFactory, stringInterner);
        var documentSet = new NLCPGDocumentSet(registry);

        // ② 取得每文件的（语义模型, 语法根）。
        //    ⚠ 跨文件解析要求全部文件在**同一个** compilation 里，这是 D1 的正确性前提，不是优化：
        //      若每文件各持一个只含自身的 compilation，则 A 里对 B 的类型/方法调用**无法解析**，
        //      调用图与成员访问事实随之缺失或错误——而「把多个文件放在一起」的全部意义
        //      正是让跨文件的完整方法能被一起处理。
        var inputs = ResolveSemanticInputs(documents);

        // ③ 逐文件建立 context；每个 context 的图由注册表 GetOrAdd 提供（共享字符串表）。
        var orderedFilePaths = new List<string>(documents.Count);
        NLCPGBuildContext? firstContext = null;
        for (var index = 0; index < documents.Count; index += 1)
        {
            var filePath = documents[index].FilePath;
            var input = inputs[index];
            var documentContext = NLCPGBuildContext.Create(
              input.SemanticModel,
              input.Root,
              documents[index].Source,
              filePath,
              preallocatedNodeIds: null,
              identityFactory,
              registry);
            documentSet.Add(documentContext);
            orderedFilePaths.Add(filePath);
            firstContext ??= documentContext;
        }

        // ④ 把文档集挂到**每一个** context：规划相位与各 pass 都从被传入的那个 context
        //    读 `Documents`/`ResolveDocument`/`ResolveGraph`，故每个都必须能看到全集。
        foreach (var filePath in orderedFilePaths)
        {
            documentSet.ResolveContext(filePath).AttachDocumentSet(documentSet);
        }

        // ⑤ 以首个文档为「驱动 context」跑一次构建；跨文件聚合由 Documents 完成。
        //    整批只有这一次 Build ⇒ 指标是整批口径（见 NLCPGMultiFileBuildResult.Metrics）。
        _ = Build(firstContext!);
        var metrics = LastBuildMetrics;

        // ⑥ 返回每文件一张的图（按输入顺序，确定性）。
        var graphsByFilePath = new Dictionary<string, NLCPGGraph>(StringComparer.Ordinal);
        foreach (var filePath in orderedFilePaths)
        {
            graphsByFilePath[filePath] = registry.Resolve(filePath);
        }

        return new NLCPGMultiFileBuildResult(graphsByFilePath, metrics);
    }

    /// <summary>
    /// 取得每个文档的（语义模型, 语法根）：调用方已提供则直接复用，
    /// 否则把全部文件放进同一个 compilation 再逐文件解析。
    /// </summary>
    private static IReadOnlyList<NLCPGSourceSemanticInput> ResolveSemanticInputs(
      IReadOnlyList<NLCPGBuildDocument> documents)
    {
        if (documents[0].SemanticModel is { } firstModel && documents[0].Root is { } firstRoot)
        {
            var supplied = new NLCPGSourceSemanticInput[documents.Count];
            supplied[0] = new NLCPGSourceSemanticInput(firstModel, firstRoot);
            for (var index = 1; index < documents.Count; index += 1)
            {
                // 上游校验已保证「要么全提供、要么全不提供」。
                supplied[index] = new NLCPGSourceSemanticInput(
                  documents[index].SemanticModel!,
                  documents[index].Root!);
            }

            return supplied;
        }

        var files = new (string FilePath, string Source)[documents.Count];
        for (var index = 0; index < documents.Count; index += 1)
        {
            files[index] = (documents[index].FilePath, documents[index].Source);
        }

        return NLCPGBuildContext.CreateSourceSemanticInputs(files);
    }

    private NLCPGGraph Build(NLCPGBuildContext context)
    {
        var buildStopwatch = Stopwatch.StartNew();
        _activeBuildGraphs.Clear();
        if (context.GraphRegistry is { } registry)
        {
            foreach (var filePath in registry.RegisteredFilePaths)
            {
                _activeBuildGraphs.Add(registry.Resolve(filePath));
            }
        }
        else
        {
            _activeBuildGraphs.Add(context.Graph);
        }

        // S5-2：本构建的文档序号表。单文档 ⇒ 置 null，使 ResolveShardOrder 走
        // 「直接返回局部序号」的退化分支（改造前行为，含 ShardOrder == StableOrder）。
        if (context.DocumentSet is { Count: > 1 } documentSet)
        {
            var ordinals = new Dictionary<string, int>(documentSet.Count, StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var filePath in documentSet.FilePaths)
            {
                ordinals[filePath] = ordinal;
                ordinal += 1;
            }

            _documentOrdinalsByFilePath = ordinals;
            _documentCountForShardOrder = documentSet.Count;

            // S5-2：**在这里**现算分片计划，用的是**正在构图的这个** builder 自己的枚举，
            // 故计划里的方法序号/成本与紧随其后的装箱**同源同配置**，且枚举结果进入本
            // builder 的操作根缓存（构图阶段直接命中，不重复扫语法树）。
            // 每次构建都重算，上一次的计划不会泄漏到下一次。
            _workShardPlan = BuildWorkShardPlan(context, documentSet);
        }
        else
        {
            _documentOrdinalsByFilePath = null;
            _documentCountForShardOrder = 1;
            _workShardPlan = null;
        }

        _syntaxNodes.Clear();
        _symbolNodes.Clear();
        _typeDeclNodes.Clear();
        _methodNodes.Clear();
        _methodParameterNodes.Clear();
        _methodReturnNodes.Clear();
        _methodEntryNodes.Clear();
        _methodExitNodes.Clear();
        _symbolKeysByNode.Clear();
        _methodOwnerSymbolKeysByBoundaryNode.Clear();
        _methodParameterOrdinalsByNode.Clear();
        _methodSymbolsByFullName.Clear();
        _methodSymbolsByNameAndSignature.Clear();
        _baseTypeCache.Clear();
        _cfgPredecessorsByNode.Clear();
        _cfgSuccessorsByNode.Clear();
        _callSiteNodesByInvocation.Clear();
        _operationNodesByOperation.Clear();
        _resolvedCallTargetsByInvocation.Clear();
        _resolvedCallTargetsByDispatchShape.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _declaredTypes.Clear();
        _dataFlowMethodMetrics.Clear();
        _dataFlowDiagnostics.Clear();
        _dataFlowBatchCount = 0;
        _dataFlowPlanAssemblyCount = 0;
        _dataFlowWorkerCount = 0;
        _callGraphBatchCount = 0;
        _interproceduralBarrierCompleted = false;
        _workBatchPerformanceEvents.Clear();
        _operationRootPlansByRoot.Clear();
        _operationNodeCacheHitCount = 0;
        _operationNodeCacheMissCount = 0;
        _operationRootCacheHitCount = 0;
        _operationRootCacheMissCount = 0;
        _passElapsedMilliseconds.Clear();
        // ⚠ 必须在此清空（而不是原来那样放到后置 pass 之前）：`Syntax`/`Operation`
        //   在**本行之下**就通过 MeasureStage 执行并记录（见其 recordedStage 参数）。
        //   若仍在 :427 处清空，这两个阶段的记录会被本次构建自己抹掉，
        //   而"缺席"又被当作"未请求"⇒ 权威表里那两条边重新变成不可失败。
        _executedStageOrder.Clear();
        // 同理必须在方法开头清空：Syntax/Operation 在下方就会报告观测，
        // 若留到规划相位前才清，这两个阶段的观测会被本次构建自己抹掉。
        _observedStageExecutions.Clear();
        CpgShardBuildCoordinator? persistenceCoordinator = null;
        NLCPGPersistenceMetrics? persistenceMetrics = null;
        var persistenceHit = false;
        if (_options.Persistence is not null)
        {
            persistenceCoordinator = new CpgShardBuildCoordinator(_options.Persistence, _concurrencyPool);
            CpgBaseRestoreResult? restoredBase = null;
            var restoreStopwatch = Stopwatch.StartNew();
            MeasureStage(
              "PersistenceRestore",
              () => restoredBase = persistenceCoordinator
                .TryRestoreBaseAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult());
            restoreStopwatch.Stop();
            var restoreMetrics = persistenceCoordinator.LastRestoreMetrics;
            persistenceMetrics = NLCPGPersistenceMetrics.Empty with
            {
                RestoreAttempted = true,
                RestoreHit = restoredBase is not null,
                RestoreElapsedMilliseconds = restoreStopwatch.ElapsedMilliseconds,
                CatalogReadMilliseconds = restoreMetrics.CatalogReadMilliseconds,
                ShardReadMilliseconds = restoreMetrics.ShardReadMilliseconds,
                RestoredShardCount = restoreMetrics.RestoredShardCount,
                RestoredShardBytes = restoreMetrics.RestoredShardBytes,
                RestoreFactsElapsedMilliseconds = restoreMetrics.RestoreFactsElapsedMilliseconds,
                RestoreFactsAllocatedBytes = restoreMetrics.RestoreFactsAllocatedBytes,
            };
            _passElapsedMilliseconds["PersistenceRestoreFacts"] =
              restoreMetrics.RestoreFactsElapsedMilliseconds;
            if (restoredBase is { } restoredFacts)
            {
                var graphImportAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                MeasureStage(
                  "PersistenceGraphImport",
                  () => context.Graph.ImportMutableFacts(restoredFacts.Facts.Nodes, restoredFacts.Facts.Edges, restoredFacts.Facts.StringInterner));
                persistenceMetrics = persistenceMetrics with
                {
                    RestoreGraphImportElapsedMilliseconds = _passElapsedMilliseconds["PersistenceGraphImport"],
                    RestoreGraphImportAllocatedBytes =
                      GC.GetTotalAllocatedBytes(precise: false) - graphImportAllocatedBefore,
                };
                restoredBase = null;
                persistenceHit = true;
            }
        }
        var buildPlan = ResolveCapabilityBuildPlan();
        // D1：多文件构建时下面是**逐文档**执行的；单文件时 `context.Documents` 恒为「自身」一项，
        //   故每个循环都恰好执行一次，行为与改造前**逐字相同**。
        var documents = context.Documents;
        if (documents.Count > 1 && _options.Persistence is not null)
        {
            // fail-closed：持久化/流式分片链路整体建立在**单一** context.Graph 之上
            //   （TryRestoreBaseAsync / ImportMutableFacts / SkeletonShardPublisher 都收 context）。
            //   多文件下它会静默地只覆盖一张图，其余各文件的图既不落盘也读不回。
            //   与其产出一份看起来完整的部分结果，不如在此明确拒绝。
            throw new NotSupportedException(
              "Persistence and streaming shards are not supported for multi-file builds, because the "
              + "persistence pipeline is defined over a single graph. Build each file separately when "
              + "persistence is required.");
        }

        SkeletonShardPublisher? streamingPublisher = null;
        var streamingPersistenceCompleted = false;

        try
        {
            // ⚠ 阶段**只登记一次**，即使内部按文档循环：TryValidateOrder 把「同一阶段出现多次」
            //   判为失败（StageDependencyTable.cs:109-116），故逐文档各调一次 MeasureStage 会
            //   直接让整次构建 fail-closed。循环必须在 MeasureStage **之内**。
            MeasureStage(
              "Syntax",
              () =>
              {
                  foreach (var document in documents)
                  {
                      var syntaxStrategy = buildPlan.RequiresMethodModel
                        ? CreateOperationBuildStrategy(document)
                        : new OperationBuildStrategy(
                            NLCPGBuilderMode.Partitioned,
                            UsePartitionedOperationBuild: false,
                            SourceLineCount: CountSourceLines(document.Source),
                            OperationRoots: Array.Empty<OperationRootPlan>());
                      RunSyntaxPass(
                        document,
                        ShouldUsePartitionedSyntaxPass(document, syntaxStrategy.OperationRoots),
                        syntaxStrategy.OperationRoots,
                        buildPlan);
                  }
              },
              StageDependencyTable.Stage.Syntax);

            if (buildPlan.RequiresMethodModel)
            {
                MeasureStage(
                  "MethodModel",
                  () =>
                  {
                      foreach (var document in documents)
                      {
                          MethodDecorationPass.Instance.Run(this, document);
                      }
                  });

                if (_options.Persistence?.StreamingMode == true && !persistenceHit)
                {
                    // 先写入不可见的基础分片；会话完成前，查询端看不到这个构建。
                    streamingPublisher = SkeletonShardPublisher.BeginAsync(
                        _options.Persistence,
                        context,
                        CancellationToken.None)
                      .GetAwaiter()
                      .GetResult();
                }

                MeasureStage(
                  "Operation",
                  () =>
                  {
                      foreach (var document in documents)
                      {
                          RunPartitionedOperationPass(
                            document,
                            CreateOperationBuildStrategy(document).OperationRoots,
                            streamingPublisher);
                          CompleteOperationBackedSyntaxTypes(document);
                      }
                  },
                  StageDependencyTable.Stage.Operation);

                if (streamingPublisher is not null)
                {
                    // 操作分片均已按源顺序写入后，补齐基础节点和跨分片邻接表，再一次性发布会话。
                    NLCPGPersistenceMetrics? streamingMetrics = null;
                    MeasureStage(
                      "StreamingBasePublish",
                      () => streamingMetrics = streamingPublisher
                        .CompleteBaseAsync(context, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
                    persistenceMetrics = MergePersistenceMetrics(persistenceMetrics, streamingMetrics!);
                    streamingPublisher = null;
                    streamingPersistenceCompleted = true;
                }
            }

            // ── G0-P R-1：阶段依赖的声明式校验（fail-closed）──────────────────────────
            // 下面这些 pass 的真实依赖是【隐式】的，此前仅由书写顺序保证。
            // 本段记录【实际执行到的】阶段顺序，并在全部 pass 结束后对照 StageDependencyTable 校验；
            // 违反即抛，而不是静默少边。
            // ⚠ 刻意【不】硬编码期望序列：序列由这里实际调用了什么决定，
            //   否则校验会退化成"对照一份手抄副本"，改了两行却仍可能自洽通过。
            // ⚠ 这里**不再**清空 _executedStageOrder：本次构建的 Syntax/Operation 已在上方
            //   通过 MeasureStage 记录，清空会把它们抹掉，使权威表里的
            //   Operation ← Syntax、CallGraph ← Operation 两条边重新不可失败。
            //   （清空已移至方法开头，见那里的注释。）
            _stageWorkResults.Clear();
            _stagePlans.Clear();
            _planningPhaseState = PlanningPhaseState.NotStarted;
            // R-4：上一次构建的配额授予表必须清空——否则失败的构建会留下
            // 一份看似成功的作用域表（它与本次的规划快照无关）。
            _stageQuotaAllocation = StageQuotaAllocation.Empty;
            PlansAtEndOfPlanningPhase = null;
            // R.4 第 3 类：发布前置条件与发布留证也必须逐次构建重置。
            // 尤其 _stageDependencyOrderVerified：若不重置，上一次成功构建会把本次的
            // 【从未校验过】的执行也放行为"可发布"，守卫即沦为递推的空洞通过。
            _stageDependencyOrderVerified = false;
            _publicationStageOrder.Clear();
            // L1 窗口计数也必须按构建重置：Enter/Exit 是严格配对的，正常情况下
            // 上一次构建结束时计数已回到 0、累计值不影响正确性；但仍显式归零，
            // 使"本次构建开过窗口吗"这一判据在**复用 builder** 时同样成立
            // （与 _publicationStageOrder 的复用用例同源）。
            _sharedStateWindowEnterCount = 0;

            // ── G0-P R-3：静态规划相位（在任何**后置阶段**的 worker 启动之前）────────
            // 此前规划发生在各 pass 的【计算阶段内部】，导致窗口开始前不知规模，
            // 无法预先配额度。本相位把可静态规划的阶段提前算完并登记，
            // 使"窗口开始前即可知规模"成立（R.4 配额预检的前提）。
            //
            // ⚠ 精确边界（勿夸大）：本相位位于 **Operation 阶段之后**——
            //   Operation 阶段（RunPartitionedOperationPass → ...WithWorkBatches）
            //   自身已经用过 _workBatchExecutor。故本相位保证的是
            //   "先于**全部后置 pass 阶段**的 worker"，而**不是**"先于进程内任何 worker"。
            //   之所以必须靠后：上面那些阶段的 plan 需要 context.InvocationOperations /
            //   PropertyReferenceOperations / OperationInventory，这些正是 Operation 阶段的产物。
            //
            // ⚠ 安全性前提（现已由机制强制，不再是注释声明）——
            //   整条规划链只【读】context 与既有缓存，并调用无状态构造器，
            //   绝不写共享图。此前这条只是注释（"已实测"），如今由
            //   NLCPGGraph 的只读窗口 fail-closed 强制（附录 R.3「分层」）：
            //   规划相位内任何 AddNode/AddEdge 都会抛出并指出违反的层次。
            //   这把"L0 只读"从**声明**变成**不变量**——否则该性质被破坏时，
            //   唯一发现途径是"某个测试恰好断言了图内容"（附录 N.4/P.4 的教训）。
            _planningPhaseState = PlanningPhaseState.Open;
            // D1：多文件时**每张**兄弟图都要进入只读窗口。
            //   旧实现只保护 context.Graph，多文件下其余各文件的图在规划相位内
            //   仍可被写——守卫对它们**完全不生效**（静默失效，非崩溃）。
            foreach (var graph in _activeBuildGraphs)
            {
                graph.EnterReadOnlyWindow();
            }

            try
            {
                PlanStagesBeforeExecution(buildPlan, context);
            }
            finally
            {
                // 用 finally 关闭相位：规划中途抛异常时，相位也必须闭合，
                // 否则后续（失败路径上的）登记会被误判为"仍在规划相位内"。
                // 只读窗口同理必须成对退出——否则**执行相位**的合法构图会全部误抛。
                foreach (var graph in _activeBuildGraphs)
                {
                    graph.ExitReadOnlyWindow();
                }

                _planningPhaseState = PlanningPhaseState.Closed;
            }

            // 规划相位到此结束。此后登记的 plan 一律视为**延迟规划**，
            // 并因此被要求如实声明 DeferredUntilRuntimeInputs（见 RecordStagePlan）。

            // R-3：留证——规划相位到此结束。此刻尚未有 worker 运行过，
            // 故 plan 应有内容而 work result 必为空。契约测试据此判定"规划确实先于执行"。
            PlanSnapshotAtEndOfPlanningPhase = _stagePlans.Keys.ToArray();
            WorkResultsAtEndOfPlanningPhase = _stageWorkResults.Keys.ToArray();

            // 冻结一份规划产物的**不可变副本**：R-4 的配额分级必须消费这个快照，
            // 而不是去读仍然可变的 _stagePlans。否则"消费规划相位"会退化成
            // "读一个之后还会变的活字段"，二者在测试里无法区分。
            PlansAtEndOfPlanningPhase =
              new Dictionary<StageDependencyTable.Stage, IStagePlan<CpgWorkBatch>>(_stagePlans);

            // ── G0-P R-4：用规划相位快照解析**配额作用域**（四要素第 ③ 项）──────────
            // 这是"规划相位尚未被消费"的关闭点：此前 R-3 只建立了"窗口前可知规模"的能力，
            // 没有任何消费者据此配额度。本行是第一个消费者，且其输入**就是**上面的快照——
            // 故"规划真的前移了"与"配额据此分级了"由同一个事实驱动，不可能各自漂移。
            //
            // ⚠ 作用域 ≠ 大小：本轮**不**推导任何字节额度。
            //   附录 G 已实测 Estimate 缺自变量（同一行范围下真实载荷跨度 1349×），
            //   不存在可用公式上界；凭空造额度正是计划 :114 点名禁止的形态。
            //   故本步骤只回答"该阶段用哪个池"，且默认全部 Shared（= 既有行为，逐字不变）。
            _stageQuotaAllocation = StageQuotaPolicy.Resolve(
              PlansAtEndOfPlanningPhase,
              StageQuotaPolicy.BatchPlanCapableStages,
              calibrations: null,
              requestedScopes: null,
              stagesExecutedBeforePlanningPhase: StageQuotaPolicy.StagesExecutedBeforePlanningPhase);

            RunOptionalPass(buildPlan.RequiresCallTargets, CallGraphPass.Instance, context, StageDependencyTable.Stage.CallGraph);
            RunOptionalPass(buildPlan.RequiresMethodModel, MemberAccessPass.Instance, context, StageDependencyTable.Stage.MemberAccess);
            RunOptionalPass(buildPlan.RequiresCfg, ControlFlowPass.Instance, context, StageDependencyTable.Stage.ControlFlow);
            RunOptionalPass(buildPlan.RequiresDataFlow, DataFlowPass.Instance, context, StageDependencyTable.Stage.DataFlow);
            RunOptionalPass(buildPlan.RequiresInterproceduralDataFlow, InterproceduralDataFlowPass.Instance, context, StageDependencyTable.Stage.InterproceduralDataFlow);
            // ⚠ 顺序约束：DominancePass 必须先于 ControlDependencePass。
            // ControlDependencePass.cs:67 依赖 `_dominanceOverlays`（由 DominancePass 填充）；
            // 若该列表为空，该阶段会【整体静默 return】——不抛异常、不打日志。
            // 本条已登记进 StageDependencyTable（依赖③），下面的校验会强制它。
            // 回归证据：附录 Q.4（互换两行 ⇒ FullyQualifiedName~Cpg 由 548/548 全绿变为 5 失败）。
            RunOptionalPass(buildPlan.RequiresDominance, DominancePass.Instance, context, StageDependencyTable.Stage.Dominance);
            RunOptionalPass(buildPlan.RequiresControlDependence, ControlDependencePass.Instance, context, StageDependencyTable.Stage.ControlDependence);

            // 全部后置 pass 已按【实际顺序】执行完毕，现在对照权威表校验。
            // 违反 ⇒ fail-closed：不发布可能已残缺的图。
            EnsureStageDependencyOrder();

            // Freeze 后图进入查询态，释放仅服务于构建过程的 Roslyn 映射和临时缓存。
            // ⚠ R.4 第 3 类：冻结是**发布动作**，必须先证明"顺序已校验 + 无 worker 存活"。
            //   放在 MeasureStage 之前（而非之内）：守卫必须早于动作，事后检查只能证明"已经晚了"。
            EnsurePublicationAllowed("FreezeQueryIndex");
            RecordPublicationStage("FreezeQueryIndex");
            // D1：多文件时冻结**全部**兄弟图（单文件时与旧行为逐字相同）。
            MeasureStage("FreezeQueryIndex", () =>
            {
                if (context.GraphRegistry is { } registry)
                {
                    registry.FreezeAll();
                    return;
                }

                context.Graph.FreezeQueryIndex();
            });
            ReleaseTransientBuilderState();

            if (_options.Persistence is not null && !streamingPersistenceCompleted && !persistenceHit)
            {
                // 持久化同样是发布动作。此处已无 worker（冻结前已断言），但**再断言一次**：
                // 两次检查之间隔着 ReleaseTransientBuilderState 与分支判断，是真实的代码距离。
                EnsurePublicationAllowed("PersistenceWrite");
                RecordPublicationStage("PersistenceWrite");
                NLCPGPersistenceMetrics? writeMetrics = null;
                MeasureStage(
                  "PersistenceWrite",
                  () => writeMetrics = persistenceCoordinator!
                    .PersistAsync(context, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
                persistenceMetrics = MergePersistenceMetrics(persistenceMetrics, writeMetrics!);
                streamingPublisher = null;
            }

            if (persistenceMetrics is not null)
            {
                persistenceMetrics = persistenceMetrics with
                {
                    Provenance = CreatePersistenceProvenance(context, buildPlan),
                };
            }

            CpgBuildInventoryMetrics? buildInventoryMetrics = null;
            MeasureStage(
              "BuildInventoryAudit",
              () => buildInventoryMetrics = CpgBuildInventory.Create(context).Metrics);
            buildStopwatch.Stop();
            LastBuildMetrics = new NLCPGBuildMetrics(
              _operationNodeCacheHitCount,
              _operationNodeCacheMissCount,
              _operationRootCacheHitCount,
              _operationRootCacheMissCount,
              context.OperationInventory.Count,
              context.Graph.Nodes.Count,
              context.Graph.Edges.Count,
              buildStopwatch.ElapsedMilliseconds,
              PassElapsedMilliseconds: new Dictionary<string, long>(
                _passElapsedMilliseconds,
                StringComparer.Ordinal),
              PersistenceMetrics: persistenceMetrics,
              DataFlowMethodMetrics: _dataFlowMethodMetrics.ToArray(),
              BuildInventoryMetrics: buildInventoryMetrics,
              DataFlowBatchCount: _dataFlowBatchCount,
              DataFlowWorkerCount: _dataFlowWorkerCount,
              CallGraphBatchCount: _callGraphBatchCount,
              InterproceduralBarrierCompleted: _interproceduralBarrierCompleted,
              WorkBatchPerformanceEvents: _workBatchPerformanceEvents.ToArray());
            return context.Graph;
        }
        finally
        {
            if (streamingPublisher is not null)
            {
                streamingPublisher.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private bool RequiresPreallocatedNodeIds()
    {
        return _options.UsePreallocatedNodeIds || _options.Persistence?.StreamingMode == true;
    }

    private NLCPGBuilderOptions CreateAnchorDiscoveryOptions()
    {
        var buildPlan = ResolveCapabilityBuildPlan();
        return _options with
        {
            Persistence = null,
            UsePreallocatedNodeIds = false,
            RequestedCapabilities = new[] { buildPlan.ResolvedCapabilities },
        };
    }

    private void ReleaseTransientBuilderState()
    {
        _activeBuildGraphs.Clear();
        _syntaxNodes.Clear();
        _symbolNodes.Clear();
        _typeDeclNodes.Clear();
        _methodNodes.Clear();
        _methodParameterNodes.Clear();
        _methodReturnNodes.Clear();
        _methodEntryNodes.Clear();
        _methodExitNodes.Clear();
        _symbolKeysByNode.Clear();
        _methodOwnerSymbolKeysByBoundaryNode.Clear();
        _methodParameterOrdinalsByNode.Clear();
        _methodSymbolsByFullName.Clear();
        _methodSymbolsByNameAndSignature.Clear();
        _baseTypeCache.Clear();
        _cfgPredecessorsByNode.Clear();
        _cfgSuccessorsByNode.Clear();
        _callSiteNodesByInvocation.Clear();
        _operationNodesByOperation.Clear();
        _resolvedCallTargetsByInvocation.Clear();
        _resolvedCallTargetsByDispatchShape.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _declaredTypes.Clear();
        _operationRootPlansByRoot.Clear();
    }

    private CapabilityBuildPlan ResolveCapabilityBuildPlan()
    {
        var requestedCapabilities = _options.RequestedCapabilities;
        var resolved = requestedCapabilities is null
            ? NLCPGCapability.Default
            : requestedCapabilities.Aggregate(NLCPGCapability.None, (current, capability) => current | capability);
        if ((resolved & ~NLCPGCapability.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.RequestedCapabilities), "The requested CPG capability set contains an unknown value.");
        }

        if ((resolved & (NLCPGCapability.MethodModel |
                         NLCPGCapability.CallTargets |
                         NLCPGCapability.Cfg |
                         NLCPGCapability.DataFlow |
                         NLCPGCapability.InterproceduralDataFlow |
                         NLCPGCapability.Dominance |
                         NLCPGCapability.ControlDependence)) != 0)
        {
            resolved |= NLCPGCapability.MethodModel;
        }

        if ((resolved & NLCPGCapability.DataFlow) != 0)
        {
            resolved |= NLCPGCapability.CallTargets | NLCPGCapability.Cfg;
        }

        if ((resolved & NLCPGCapability.InterproceduralDataFlow) != 0)
        {
            resolved |= NLCPGCapability.DataFlow |
                        NLCPGCapability.CallTargets |
                        NLCPGCapability.MethodModel |
                        NLCPGCapability.QueryIndex;
        }

        if ((resolved & NLCPGCapability.Dominance) != 0)
        {
            resolved |= NLCPGCapability.Cfg;
        }

        if ((resolved & NLCPGCapability.ControlDependence) != 0)
        {
            resolved |= NLCPGCapability.Dominance | NLCPGCapability.Cfg;
        }

        if (resolved != NLCPGCapability.None)
        {
            resolved |= NLCPGCapability.SyntaxSemantic;
        }

        return new CapabilityBuildPlan(
            resolved,
            EmitSyntaxTokens: (resolved & NLCPGCapability.SyntaxToken) != 0,
            EmitReferences: (resolved & NLCPGCapability.Reference) != 0,
            EmitTypeReferences: (resolved & NLCPGCapability.TypeRef) != 0,
            RequiresMethodModel: (resolved & NLCPGCapability.MethodModel) != 0,
            RequiresCallTargets: (resolved & NLCPGCapability.CallTargets) != 0,
            RequiresCfg: (resolved & NLCPGCapability.Cfg) != 0,
            RequiresDataFlow: (resolved & NLCPGCapability.DataFlow) != 0,
            RequiresInterproceduralDataFlow: (resolved & NLCPGCapability.InterproceduralDataFlow) != 0,
            RequiresDominance: (resolved & NLCPGCapability.Dominance) != 0,
            RequiresControlDependence: (resolved & NLCPGCapability.ControlDependence) != 0);
    }

    /// <summary>
    /// G0-P R-1：**实际执行到的**后置阶段顺序（按调用先后追加）。
    /// <para>
    /// 刻意由 <see cref="RunOptionalPass(bool, INLCPGPass, NLCPGBuildContext, StageDependencyTable.Stage)"/>
    /// 在**真正执行时**记录，而不是对照一份手抄的期望序列——
    /// 否则校验会退化成"与自己抄的副本比对"，改了两行仍可能自洽通过。
    /// </para>
    /// </summary>
    private readonly List<StageDependencyTable.Stage> _executedStageOrder = new(9);

    /// <summary>
    /// G0-P **R-2**：各阶段的实际工作结果（按阶段索引）。
    /// <para>
    /// 此前 5 个阶段各持私有嵌套 record、4 个阶段连结果类型都没有；本字段把它们
    /// 统一成 <see cref="IStageWorkResult"/>，供窗口层记账与 R.4 配额作用域使用。
    /// </para>
    /// </summary>
    private readonly Dictionary<StageDependencyTable.Stage, IStageWorkResult> _stageWorkResults = new();

    /// <summary>
    /// G0-P **R-2**：最近一次构建中各阶段的实际工作结果，按阶段索引。
    /// <para>
    /// 供窗口层记账与 R.4 配额回填使用；也供契约测试核验记账对象与实际产出是否一致。
    /// </para>
    /// </summary>
    internal IReadOnlyDictionary<StageDependencyTable.Stage, IStageWorkResult> LastStageWorkResults =>
      _stageWorkResults;

    /// <summary>
    /// G0-P **R-3**：各阶段的**静态规划结果**，在对应阶段执行前登记。
    /// <para>
    /// 这是"每阶段静态规划时点、结果类型"从**隐式**变为**可观测**的核心：
    /// 规划在任何 worker 启动之前完成并留下记录，窗口层据此预检额度（R.4 的前提）。
    /// </para>
    /// </summary>
    internal IReadOnlyDictionary<StageDependencyTable.Stage, IStagePlan<CpgWorkBatch>> LastStagePlans =>
      _stagePlans;

    private readonly Dictionary<StageDependencyTable.Stage, IStagePlan<CpgWorkBatch>> _stagePlans = new();

    /// <summary>
    /// G0-P **R-3**：**规划相位结束瞬间**已登记阶段的快照。
    /// <para>
    /// 存在的唯一目的是让"规划先于执行"成为**可判定的事实**而非口头声明：
    /// 若 <c>Plan*</c> 只是改了名、仍留在执行相位内，则本快照会为空，
    /// 而 <see cref="WorkResultsAtEndOfPlanningPhase"/> 会非空 ⇒ 契约测试立刻失败。
    /// </para>
    /// </summary>
    internal IReadOnlyCollection<StageDependencyTable.Stage>? PlanSnapshotAtEndOfPlanningPhase { get; private set; }

    /// <summary>
    /// G0-P **R-3**：规划相位结束瞬间的**工作结果**快照——必须为空（尚无 worker 运行过）。
    /// </summary>
    internal IReadOnlyCollection<StageDependencyTable.Stage>? WorkResultsAtEndOfPlanningPhase { get; private set; }

    /// <summary>
    /// G0-P **R-4**：规划相位结束瞬间 plan 的**不可变副本**——配额分级的唯一输入。
    /// <para>
    /// 与 <see cref="LastStagePlans"/>（活字段，执行相位会继续往里登记）分开保存，
    /// 使"消费的是规划相位快照"成为一个可判定的事实。
    /// </para>
    /// </summary>
    internal IReadOnlyDictionary<StageDependencyTable.Stage, IStagePlan<CpgWorkBatch>>? PlansAtEndOfPlanningPhase { get; private set; }

    private StageQuotaAllocation _stageQuotaAllocation = StageQuotaAllocation.Empty;
    private readonly List<StageDependencyTable.Stage> _observedStageExecutions = new();

    /// <summary>
    /// G0-P **R-4**：最近一次构建的**配额作用域授予表**——由规划相位快照解析得出。
    /// <para>
    /// ⚠ 与 <see cref="LastStageWorkResults"/> 一样是**只读结果**，不是配置开关；
    /// 它**不改变**任何执行行为（本轮无 <c>Dedicated</c> 启用 ⇒ 全部 <c>Shared</c>）。
    /// </para>
    /// </summary>
    internal StageQuotaAllocation LastStageQuotaAllocation => _stageQuotaAllocation;

    /// <summary>
    /// G0-P **R-4**：执行器报告"某阶段确实提交了批次"时的**对账点**。
    /// <para>
    /// 这是 <see cref="StageQuotaPolicy.BatchPlanCapableStages"/> 这张手写声明表的
    /// **第一个生产消费者**。此前该表只被 <c>Resolve</c> 用来分类成因，没有任何机制
    /// 校验它与真实执行一致——漏写的阶段会静默落入 <see cref="StageQuotaBasis.NoBatchPlan"/>
    /// 而豁免整套 R-4 治理（附录 AA："声明代替机制"）。
    /// </para>
    /// <para>
    /// ⚠ <b>读的是当前构建的配额表</b>（<c>_stageQuotaAllocation</c>），
    /// 而非构造期捕获的某个快照——builder 可被复用，每次构建的分类都可能不同。
    /// 规划相位之前（表为 <c>Empty</c>）不做判定：那时没有可对账的声明
    /// （<c>Syntax</c>/<c>Operation</c> 正当如此，附录 V.2）。
    /// </para>
    /// </summary>
    /// <param name="stageId">执行器收到的阶段标签。</param>
    internal void ReconcileObservedStageExecution(string? stageId)
    {
        if (StageQuotaPolicy.TryResolveDeclaredStage(stageId, out var stage))
        {
            // 记账发生在**调用线程**、worker 启动之前（执行器的观察点如此设计），
            // 故这里的普通 List 不会成为新的并发写面。
            _observedStageExecutions.Add(stage);
        }

        StageQuotaPolicy.ReconcileObservedStageExecution(_stageQuotaAllocation, stageId);
    }

    /// <summary>
    /// G0-P **R-4** 留证：本构建中**真实经过批次执行器**的阶段（按报告先后，含重复）。
    /// <para>
    /// 这不是多余的内省，而是让"声明表与真实执行一致"成为**可判定事实**的唯一办法：
    /// 生产配置下声明表是对的，故 fail-closed 对账**永远不会触发**——若只有抛出逻辑，
    /// 钩子若没被接上（或整个机制被删）在行为上与"一切正常"**完全不可区分**
    /// （附录 N.4/P.4：未被观测面覆盖的断言等于不存在）。
    /// </para>
    /// <para>
    /// 于是契约测试可以断言：全能力构建下观测集合**等于**声明的
    /// <see cref="StageQuotaPolicy.BatchPlanCapableStages"/>。钩子未接线 ⇒ 集合为空 ⇒ 失败；
    /// 某阶段被悄悄移出声明表 ⇒ 两集合不等 ⇒ 失败。
    /// </para>
    /// </summary>
    internal IReadOnlyList<StageDependencyTable.Stage> ObservedStageExecutions => _observedStageExecutions;

    /// <summary>
    /// G0-P **R-3** 留证：本构建中 DataFlow 批次被**构造**的次数。
    /// <para>
    /// 契约测试用它判定"提交步确实消费了 plan，而没有无视它自行重算"。
    /// 这不是多余的内省：重算与消费在**产物上完全等价**，故行为断言无从区分
    /// （附录 X.5 变异②实测该变异存活于全部 197 条用例之下）。
    /// 正常值恒为 <c>1</c>（只由规划相位调用一次）；<c>0</c> 表示未请求 DataFlow。
    /// </para>
    /// </summary>
    internal int DataFlowPlanAssemblyCount => _dataFlowPlanAssemblyCount;

    /// <summary>
    /// G0-P **R-1** 留证：本构建中**实际被记录到的**阶段执行序列（按真实调用先后）。
    /// <para>
    /// <b>为什么必须可观测：</b>权威表里 <c>Operation ← Syntax</c>、<c>CallGraph ← Operation</c>
    /// 两条边长期**不可失败**——因为 <c>Syntax</c>/<c>Operation</c> 不经
    /// <c>RunOptionalPass</c> 执行，从不进入被校验的序列，而
    /// <c>TryValidateOrder</c> 把"缺席的前置"当作"未请求⇒已满足"。
    /// "从未记录"与"没有机制"在**行为层完全等价**（附录 Y.4 的同型问题），
    /// 故只能靠这个计数/序列面判别：断言它**确实包含**这两个阶段，
    /// 否则"补记"这个改动可以整个消失而全部行为用例仍然全绿。
    /// </para>
    /// <para>它是**只读结果**，不参与任何生产决策。</para>
    /// </summary>
    internal IReadOnlyList<StageDependencyTable.Stage> LastExecutedStageOrder => _executedStageOrder;

    /// <summary>
    /// G0-P **R-3**：登记某阶段的规划结果。
    /// <para>
    /// ⚠ 对同一阶段重复登记会抛出——一个阶段在一次构建中只应规划一次，
    /// 重复意味着规划被放到了错误的时点（例如滑进了 worker 内部）。
    /// </para>
    /// <para>
    /// <b>同时校验声明与实际时点一致（fail-closed）：</b>规划相位内登记的 plan
    /// 必须声明 <see cref="StagePlanTiming.StaticBeforeExecution"/>；规划相位之后登记的
    /// 必须声明 <see cref="StagePlanTiming.DeferredUntilRuntimeInputs"/>。
    /// 这条把"我已前移"从**注释里的口头声明**升级为**机制**：既拦住
    /// "只改名、没前移却声称前移"，也拦住"声称延迟、实则已前移"。
    /// </para>
    /// </summary>
    internal void RecordStagePlan(IStagePlan<CpgWorkBatch> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan is StagePlan concrete && !concrete.IsSelfConsistent)
        {
            throw new InvalidOperationException(
              $"阶段 {plan.Stage} 的 plan 声明自相矛盾：Timing={concrete.Timing}，"
              + $"RequiresRuntimeInputFrom={concrete.RequiresRuntimeInputFrom?.ToString() ?? "null"}。"
              + "静态可前移的 plan 不得声称依赖运行期产物，反之亦然（G0-P R-3）。");
        }

        // ── G0-P 四要素 ④「窗口外依赖」：延迟规划必须点名一个**真实存在**的来源 ──────
        // 在此之前，`RequiresRuntimeInputFrom` 是**只写不读**的：它把
        // "本阶段依赖 Dominance 的运行期产物"这一事实**又声明了一遍**，
        // 而 StageDependencyTable 里已有一条同样的登记，两者**互不校验**：
        //   • 计划里可以点名一个**根本不是自己前置**的阶段（凭空造依赖）；
        //   • 表里的那条被改动后，计划里的旧声明不会跟着失败（两份真相各自漂移）。
        // 这正是附录 N.4/P.4 的"同一事实两处声明、无人对账"形态。
        // 故此处对账两条，且都放在登记的唯一收口上（与 R-1 的拓扑校验同源）。
        if (plan is StagePlan { RequiresRuntimeInputFrom: { } runtimeSource } deferred)
        {
            // ① 来源必须**真是**本阶段在权威表中的前置——否则该声明是虚构的。
            if (!StageDependencyTable.PredecessorsOf(deferred.Stage).Contains(runtimeSource))
            {
                throw new InvalidOperationException(
                  $"阶段 {deferred.Stage} 的 plan 声称依赖 {runtimeSource} 的运行期产物，"
                  + $"但 StageDependencyTable 未把 {runtimeSource} 登记为它的前置"
                  + $"（登记的前置为：[{string.Join(", ", StageDependencyTable.PredecessorsOf(deferred.Stage))}]）。"
                  + "两处声明必须一致：窗口外依赖的来源只能来自权威表，不得凭空指定（G0-P ④）。");
            }

            // ② 来源必须**真的已经执行过**——否则"依赖它的运行期产物"是空话。
            //    这条把声明从"名字"升级为"关于本次构建运行期事实的断言"：
            //    若某次重排让延迟规划的阶段跑在其来源之前，这里会立刻拒绝，
            //    而不是让该阶段读到空的运行期缓存后【静默少边】（附录 Q.4 的实测形态）。
            //
            //    ⚠ 仅在**执行相位**（规划相位已闭合）中判定：那时"执行历史"才有意义。
            //      在 N o t S t a r t e d 状态（构建尚未开始，例如孤立地调用本方法做
            //      契约测试）不存在执行历史，此时断言"来源未执行"只会把
            //      "无从判定"误报成"确实违反"——那正是错误的诊断（附录 Y.3 的同型问题）。
            //      生产路径上延迟规划的登记必然发生在执行相位内，故该守卫覆盖真实路径。
            if (_planningPhaseState == PlanningPhaseState.Closed
                && !_executedStageOrder.Contains(runtimeSource))
            {
                throw new InvalidOperationException(
                  $"阶段 {deferred.Stage} 的 plan 声称依赖 {runtimeSource} 的**运行期**产物，"
                  + $"但 {runtimeSource} 尚未执行（本次已执行：[{string.Join(", ", _executedStageOrder)}]）。"
                  + "延迟规划必须发生在其来源阶段之后；否则该阶段会读到空的运行期缓存并静默产出残缺结果"
                  + "（G0-P ④ 窗口外依赖）。");
            }
        }

        // 只有"规划相位正在开放中"登记的 plan 才算**真正的前移**。
        // 其余任何情况（构建未开始 / 规划相位已关闭）声称 Static 都是不实声明 ⇒ fail-closed。
        var declaredStatic = plan.Timing == StagePlanTiming.StaticBeforeExecution;
        var planningPhaseOpen = _planningPhaseState == PlanningPhaseState.Open;
        if (declaredStatic != planningPhaseOpen)
        {
            var actual = _planningPhaseState switch
            {
                PlanningPhaseState.Open => "规划相位【之内】",
                PlanningPhaseState.Closed => "规划相位结束【之后】",
                _ => "任何规划相位【之外】（构建尚未开始）",
            };

            throw new InvalidOperationException(
              $"阶段 {plan.Stage} 的规划时点声明与实际不符："
              + $"声明为 {plan.Timing}，但登记发生在{actual}（G0-P R-3）。"
              + "只有在规划相位内登记的 plan 才能声明 StaticBeforeExecution；"
              + "其余情况必须声明 DeferredUntilRuntimeInputs。");
        }

        if (!_stagePlans.TryAdd(plan.Stage, plan))
        {
            throw new InvalidOperationException(
              $"阶段 {plan.Stage} 在一次构建中被规划了多次；"
              + "规划必须在任何 worker 启动之前完成且只做一次（G0-P R-3）。");
        }
    }

    /// <summary>
    /// G0-P **R-3**：规划相位的三态。
    /// <para>
    /// 刻意用三态而非一个 bool：<c>NotStarted</c>（构建尚未开始）与 <c>Closed</c>（规划已结束）
    /// 对"静态前移"的含义**不同**——前者无从判定，后者是明确的不符。
    /// 若压成一个 bool，就无法把"构建外部的孤立登记"与"执行相位内的假前移"区分开。
    /// </para>
    /// </summary>
    private enum PlanningPhaseState
    {
        NotStarted,
        Open,
        Closed,
    }

    private PlanningPhaseState _planningPhaseState = PlanningPhaseState.NotStarted;

    /// <summary>
    /// G0-P **R-3**：**静态规划相位**——在任何 worker 启动之前，把可静态规划的阶段一次算完。
    /// <para>
    /// <b>与拆分前的实质差别：</b>此前 <c>Plan*</c> 仍在 <c>RunOptionalPass</c> 内按原顺序被调用，
    /// 只是换了函数名，规划**时点并没有前移**。本方法把它真正前移到执行之前，
    /// 才使"窗口开始前可知规模"成立——这是 R.4 配额预检的唯一可能前提。
    /// </para>
    /// <para>
    /// <b>可静态规划的范围是本轮实测界定的：</b>只有那些**不依赖前序阶段运行期状态**的阶段
    /// 才能在此规划。`ControlDependence` 依赖 `Dominance` 运行期填充的 `_dominanceOverlays`
    /// ⇒ 它**不能**在此规划（其 plan 里要用到该字段的 Count），仍留在执行相位内。
    /// </para>
    /// </summary>
    private void PlanStagesBeforeExecution(CapabilityBuildPlan buildPlan, NLCPGBuildContext context)
    {
        // 本方法只登记**只读可规划**的阶段——判据是"plan 的输入是否只来自语法/语义模型与
        // 既有缓存"，而不是"这个阶段叫什么名字"。逐条依据：
        //
        // ① CallGraph    —— 输入是 Operation 阶段填充的 InvocationOperations /
        //                   PropertyReferenceOperations 两个快照，规划相位位于其后，故已就绪。
        // ② ControlFlow  —— 规划只依赖语法/语义模型（GetOperationRootPlans + AssembleWorkBatches）。
        // ③ MemberAccess —— 同上；其**事实抽取**仍在提交步（见 CommitMemberAccessStage）。
        // ④ Dominance    —— 规划同上；本阶段真正的运行期依赖（各方法 CFG 与已物化节点）
        //                   只出现在 AnalyzeDominanceRoot，即**计算**相位，不在规划相位。
        // ⑤ DataFlow     —— 规划只依赖 context.OperationInventory（Operation 阶段的产物）
        //                   与无状态批次构造器；CFG 邻接属计算相位，不在规划内。
        //
        // ⑥ InterproceduralDataFlow / ControlDependence —— 见下方说明，刻意缺席。
        if (buildPlan.RequiresCallTargets)
        {
            var plan = PlanCallGraphStage(context);
            if (plan is not null)
            {
                RecordStagePlan(plan);
            }
        }

        if (buildPlan.RequiresCfg)
        {
            var plan = PlanControlFlowStage(context);
            if (plan is not null)
            {
                RecordStagePlan(plan);
            }
        }

        // ⑥ DataFlow —— 本轮（轮次 25）**已前移**。
        //   依据（源码确证，逐条）：批次 = AssembleDataFlowWorkBatches(CreateDataFlowMethodPartitions(...))，
        //   两者对 context.OperationInventory 都是**纯派生**，而该集合由 Operation 阶段填充，
        //   Operation 阶段位于本规划相位之前 ⇒ 输入已就绪。
        //   ⚠ 本阶段真正的运行期依赖（CFG 邻接缓存）只在 BuildCfgAdjacency 被读，属**计算**相位，
        //     故它**不属于**规划。规划只需规模，不需内容——这正是 R-3 的立足点。
        if (buildPlan.RequiresDataFlow)
        {
            RecordStagePlan(PlanDataFlowStage(context));
        }

        if (buildPlan.RequiresMethodModel)
        {
            var plan = PlanMemberAccessStage(context);
            if (plan is not null)
            {
                RecordStagePlan(plan);
            }
        }

        if (buildPlan.RequiresDominance)
        {
            var plan = PlanDominanceStage(context);
            if (plan is not null)
            {
                RecordStagePlan(plan);
            }
        }

        // ⚠ 以下 2 个阶段本轮**未**纳入规划相位，但原因各不相同（勿合并成一句"暂不支持"）：
        //
        // ⑥ ControlDependence —— **设计上不能**在此规划：它依赖 Dominance **运行期**填充的
        //    `_dominanceOverlays`（其 plan 的规模就是该字段的 Count），
        //    故只能在 Dominance 执行完之后规划。
        //    它仍在执行相位内规划，但以 DeferredUntilRuntimeInputs 如实登记，
        //    使"没能前移"成为**可观测的声明**而非注释里的口头约定。
        //
        // ⑦ InterproceduralDataFlow —— R-3 对它是 **N/A**：
        //    该阶段没有 `CpgWorkBatch` 形态的 plan（实现是 builder.RunInterproceduralDataFlowPass
        //    的纯转发，不走批次执行器），故不存在"规划时点"可前移。
        //
        // ✅ DataFlow 已于轮次 25 前移（见上方 ⑤）；此前登记为"技术可行但编辑边界未动"的
        //    遗留缺口**已关闭**。`PartitionedSyntax`/`PartitionedOperation` 仍未接入（见附录 X.6）。
    }

    /// <summary>
    /// G0-P **R-3**：取出执行相位之前登记好的阶段 plan（不存在则返回 <c>null</c>）。
    /// </summary>
    private StagePlan? TakeRecordedStagePlan(StageDependencyTable.Stage stage)
    {
        return _stagePlans.TryGetValue(stage, out var plan) ? plan as StagePlan : null;
    }

    /// <summary>R-2：当前正在记录的阶段；仅在该阶段执行期间非 null。</summary>
    private StageDependencyTable.Stage? _recordingStage;

    /// <summary>
    /// G0-P R-1：在**全部后置 pass 执行完毕后**，用 <see cref="StageDependencyTable"/> 校验实际顺序。
    /// <para>
    /// <b>为什么必须抛：</b>依赖被破坏时，既有实现的表现是
    /// <b>静默少边</b>或<b>整阶段静默跳过</b>——不抛异常、不打日志，
    /// 只有测试（若恰有覆盖）才能发现。轮次 19 的真实回归即属此类（附录 Q.4）。
    /// fail-closed 让违反立刻暴露，并且**不发布可能已残缺的图**。
    /// </para>
    /// <para>
    /// 只校验实际运行过的阶段：capability 未开启的阶段整体不运行，其缺席不构成违反。
    /// </para>
    /// </summary>
    private void EnsureStageDependencyOrder()
    {
        if (StageDependencyTable.TryValidateOrder(_executedStageOrder, out var reason))
        {
            // R.4 第 3 类的前置条件：只有**校验通过**才允许后续发布动作。
            // 在成功分支内置位（而不是在调用点），保证新增调用点无法绕过——
            // "声明在、机制不在"正是本附录要消灭的形态。
            _stageDependencyOrderVerified = true;
            return;
        }

        throw new InvalidOperationException(
          $"CPG builder stage dependency violated: {reason}"
          + " 阶段先后由 NLCPGBuilder 中的实际调用顺序决定；依赖权威表见 StageDependencyTable。");
    }

    private void RunOptionalPass(
      bool shouldRun,
      INLCPGPass pass,
      NLCPGBuildContext context,
      StageDependencyTable.Stage stage)
    {
        if (!shouldRun)
        {
            return;
        }

        // 记录实际执行顺序：无论 pass 内部做什么，这里一定先于 pass.Run 追加，
        // 故序列反映的是【真实调用先后】，与书写顺序天然一致。
        _executedStageOrder.Add(stage);

        // R-2：当前正在执行的阶段——fragment 型 pass 通过 ReportStageFragments 回填实际产出。
        // 用 pass 自己报告的"本次产出"，而不是事后重算，确保记账对象 === 执行对象。
        var previousStage = _recordingStage;
        _recordingStage = stage;
        try
        {
            MeasureStage(pass.Name, () => pass.Run(this, context));
        }
        finally
        {
            _recordingStage = previousStage;
        }
    }

    /// <summary>
    /// G0-P **R-2**：fragment 型阶段回报**本次实际产出**的 fragment，供统一记账。
    /// <para>
    /// 由 `ControlFlow` / `ControlDependence` / `MemberAccess` 三个 pass 在归并**之前**调用。
    /// 这 3 个阶段此前没有结果类型；现在被登记为 <see cref="IStageWorkResult"/>。
    /// </para>
    /// <para>
    /// ⚠ 刻意由 pass **主动回报实际产出**，而不是由 builder 事后重算：
    /// 重算会引入第二份"真相"，正是附录 N.4/P.4/S.2 反复踩到的形态。
    /// </para>
    /// </summary>
    internal void ReportStageFragments(
      IReadOnlyList<NLCPG.Builder.Concurrency.LocalCpgFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        // 只有在记录窗口内（即确实处于某个 RunOptionalPass 调用中）才接受回报。
        if (_recordingStage is not { } stage)
        {
            return;
        }

        _stageWorkResults[stage] = new StageWorkResults.FragmentStageWorkResult(stage, fragments);
    }

    /// <summary>
    /// G0-P **R-2**：`MemberAccess` 阶段回报其**实际产出**的事实条数。
    /// <para>
    /// 该阶段的产出是 `MemberAccessFact` 而非 `LocalCpgFragment`（实测 `MemberAccessPass.cs:78-97`），
    /// 故不与另外两个 fragment 型阶段共用适配器。
    /// </para>
    /// </summary>
    internal void ReportMemberAccessFacts(NLCPGBuildContext context, long producedFactCount)
    {
        if (_recordingStage is not { } stage)
        {
            return;
        }

        // 节点数取当前图节点数作为该阶段的产出规模代理——事实条数与节点数同量级，
        // 而 R.4 的配额只关心规模量级。刻意标注为代理，不声称是精确节点增量。
        _stageWorkResults[stage] = new StageWorkResults.FactCountStageWorkResult(
          stage,
          producedFactCount,
          context.Graph.Nodes.Count);
    }

    /// <summary>
    /// G0-P **R-2**：跨过程发布段回报记账结果（该阶段不产 fragment，直接发布桥接边）。
    /// </summary>
    private void RecordInterproceduralStageResult(long producedNodeCount, long publishedEdgeCount)
    {
        _stageWorkResults[StageDependencyTable.Stage.InterproceduralDataFlow] =
          new StageWorkResults.InterproceduralStageWorkResult(producedNodeCount, publishedEdgeCount);
    }

    private void MeasureStage(string stageName, Action action, StageDependencyTable.Stage? recordedStage = null)
    {
        // G0-P R-1 补漏：`Syntax`/`Operation` 在本 builder 中**不**经 RunOptionalPass 执行，
        // 而是用本方法包裹。此前本方法只记耗时 ⇒ 这两个阶段**从未**进入
        // _executedStageOrder，而 TryValidateOrder 把"缺席的前置"当作"未请求、视为已满足"，
        // 于是权威表里声明过的两条边（Operation ← Syntax、CallGraph ← Operation）
        // **永远不可能失败**——声明在，机制不在。
        // 故在此补记：本方法是这两个阶段**唯一**的执行入口，与 RunOptionalPass 同性质。
        if (recordedStage is { } stage)
        {
            _executedStageOrder.Add(stage);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            stopwatch.Stop();
            _passElapsedMilliseconds[stageName] = stopwatch.ElapsedMilliseconds;
        }
    }

    internal void RecordPartitionPerformanceEvent(
      string stageId,
      string partitionId,
      int partitionIndex,
      int inputCount,
      int outputCount,
      long wallElapsedMs,
      long accumulatedElapsedMs)
    {
        if (!PartitionPerformanceDiagnosticsEnabled)
        {
            return;
        }

        _options.PartitionPerformanceEventSink.TryRecord(
          new PartitionPerformanceEvent(
            stageId,
            partitionId,
            partitionIndex,
            inputCount,
            outputCount,
            Math.Max(0, wallElapsedMs),
            Math.Max(0, accumulatedElapsedMs),
            QueueWaitMs: null,
            RequestedMaxDegreeOfParallelism: PartitionPerformanceMaxDegreeOfParallelism,
            PeakActiveWorkItemCount: null,
            PeakBufferCount: null,
            AdmissionReason: null,
            RunId: PartitionPerformanceRunId));
    }

    internal static string CreatePartitionPerformanceId(
      string stage,
      int partitionIndex,
      int spanStart,
      int spanEnd)
    {
        return $"{stage}:{partitionIndex}:{spanStart}-{spanEnd}";
    }

    private static NLCPGPersistenceMetrics MergePersistenceMetrics(
      NLCPGPersistenceMetrics? restoreMetrics,
      NLCPGPersistenceMetrics persistMetrics)
    {
        return persistMetrics with
        {
            RestoreAttempted = restoreMetrics?.RestoreAttempted ?? false,
            RestoreHit = restoreMetrics?.RestoreHit ?? false,
            RestoreElapsedMilliseconds = restoreMetrics?.RestoreElapsedMilliseconds ?? 0,
            CatalogReadMilliseconds = restoreMetrics?.CatalogReadMilliseconds ?? 0,
            ShardReadMilliseconds = restoreMetrics?.ShardReadMilliseconds ?? 0,
            RestoredShardCount = restoreMetrics?.RestoredShardCount ?? 0,
            RestoredShardBytes = restoreMetrics?.RestoredShardBytes ?? 0,
            RestoreFactsElapsedMilliseconds = restoreMetrics?.RestoreFactsElapsedMilliseconds ?? 0,
            RestoreFactsAllocatedBytes = restoreMetrics?.RestoreFactsAllocatedBytes ?? 0,
            RestoreGraphImportElapsedMilliseconds = restoreMetrics?.RestoreGraphImportElapsedMilliseconds ?? 0,
            RestoreGraphImportAllocatedBytes = restoreMetrics?.RestoreGraphImportAllocatedBytes ?? 0,
        };
    }

    private CpgPersistenceProvenance? CreatePersistenceProvenance(
      NLCPGBuildContext context,
      CapabilityBuildPlan buildPlan)
    {
        if (_options.Persistence is not { } persistence)
        {
            return null;
        }

        var dataFlowOptions = _options.EffectiveDataFlowOptions;
        var interproceduralOptions = _options.EffectiveInterproceduralDataFlowOptions;
        var flowSummaryOptions = _options.EffectiveFlowSummaryOptions;
        var fingerprintInput = string.Join(
          "\u001F",
          ((int)buildPlan.ResolvedCapabilities).ToString(CultureInfo.InvariantCulture),
          _options.EnableReferencedSymbolTypeReuse ? "1" : "0",
          _options.EnableOperationBackedSyntaxTypes ? "1" : "0",
          _options.SyntaxPassMode.ToString(),
          dataFlowOptions.MaxDefinitionsPerMethod.ToString(CultureInfo.InvariantCulture),
          dataFlowOptions.MaxFlowNodesPerMethod.ToString(CultureInfo.InvariantCulture),
          dataFlowOptions.MaxCandidateEdgesPerMethod.ToString(CultureInfo.InvariantCulture),
          dataFlowOptions.OverflowBehavior.ToString(),
          interproceduralOptions.MaxCallTargetsPerSite.ToString(CultureInfo.InvariantCulture),
          interproceduralOptions.MaxBoundaryEdgesPerMethod.ToString(CultureInfo.InvariantCulture),
          flowSummaryOptions.MaxMappingsPerCallSite.ToString(CultureInfo.InvariantCulture),
          flowSummaryOptions.MaxMappingsPerMethod.ToString(CultureInfo.InvariantCulture),
          flowSummaryOptions.MaxMappingsPerBuild.ToString(CultureInfo.InvariantCulture));
        var compilerIdentity = typeof(CSharpCompilation).Assembly.FullName ??
          typeof(CSharpCompilation).Assembly.GetName().Name ??
          "unknown";

        return new CpgPersistenceProvenance(
          HashText(context.Source),
          persistence.ProfileHash,
          persistence.SchemaVersion,
          compilerIdentity,
          HashText(fingerprintInput));
    }

    private static string HashText(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, long>? CopyStageElapsedMilliseconds(
      IReadOnlyDictionary<string, long>? elapsedMilliseconds)
    {
        return elapsedMilliseconds is null
          ? null
          : new Dictionary<string, long>(elapsedMilliseconds, StringComparer.Ordinal);
    }

    // ── M2：四个跨过程索引共享的【单份完整边快照】──────────────────────────────
    //
    // 旧实现在 RunInterproceduralDataFlowPass 里维护四个
    // Dictionary<TKey, List<PendingEdge>>：同一条边若同时命中 DataFlowByTarget 与某个
    // 方法桶，就把整条 PendingEdge（实测 272 B，内嵌两个 NLCPGNode）完整复制两份。
    // 实测（NPC.cs）W=272 B、U=46,251、L=54,691、Σ旧容量=160,744 ⇒ 旧主载荷 41.70 MiB。
    //
    // 本类型改为：池里每条被选中的源边只保存【一次】原值，四个索引只保存池内序号
    // （4 B/成员）。实测同一夹具下新主载荷 12.98 MiB，降幅 68.87%。
    //
    // 关键边界（不得违反）：
    //   · 池的 ordinal 是【本 pass 内的边序号】，不是 NodeId、图 node ordinal 或 metadata id。
    //   · 不做值相等/身份相等合并，也不对池额外 Distinct：每条被选中的源边保存一次原值。
    //   · 池保存扫描当时的完整 PendingEdge 值快照（readonly record struct，按值复制），
    //     发布阶段【不回读图节点】；图内节点之后被 MergeNode 替换也不影响本快照。
    //   · 所有数组为精确长度：不做 TrimExcess，也不做 ToArray 二次复制。
    //   · 池与四个索引同属一个对象，最后一次窗口发布后一起离开作用域，不进入常驻字段。
    //
    // 两遍扫描之间不允许改图：计数与填充都枚举同一个未改变的惰性边序列，
    // 故两遍的边序逐项相同，桶内序号天然保持原插入序，无需排序即可还原前基线顺序。
    internal sealed class InterproceduralEdgeSnapshot
    {
        // 缺失键的共享空数组：调用方可以安全地把它当成"该键没有成员"，且不分配。
        private static readonly int[] NoOrdinals = Array.Empty<int>();

        private readonly NLCPGGraph.PendingEdge[] _pool;
        private readonly Dictionary<NLCPGNode, int[]> _callTargetsBySource;
        private readonly Dictionary<NLCPGNode, int[]> _dataFlowByTarget;
        private readonly Dictionary<string, int[]> _argumentsByMethod;
        private readonly Dictionary<string, int[]> _returnsByMethod;

        private InterproceduralEdgeSnapshot(
          NLCPGGraph.PendingEdge[] pool,
          Dictionary<NLCPGNode, int[]> callTargetsBySource,
          Dictionary<NLCPGNode, int[]> dataFlowByTarget,
          Dictionary<string, int[]> argumentsByMethod,
          Dictionary<string, int[]> returnsByMethod)
        {
            _pool = pool;
            _callTargetsBySource = callTargetsBySource;
            _dataFlowByTarget = dataFlowByTarget;
            _argumentsByMethod = argumentsByMethod;
            _returnsByMethod = returnsByMethod;
        }

        // 池元素数 = 保留边数 U。每条保留边在池里恰好一份，故这也是"每边仅保存一次"的证据。
        internal int PoolCount => _pool.Length;

        // 完整边快照的只读视图（排序键收集按池遍历一次，不再分别遍历四个索引）。
        internal IReadOnlyList<NLCPGGraph.PendingEdge> Pool => _pool;

        // 池内序号 → 完整边快照。越界即表示索引与池不一致，由运行时下标检查兜住。
        internal NLCPGGraph.PendingEdge Edge(int ordinal)
        {
            return _pool[ordinal];
        }

        internal int[] CallTargetsForSource(NLCPGNode source)
        {
            return _callTargetsBySource.TryGetValue(source, out var ordinals) ? ordinals : NoOrdinals;
        }

        internal int[] DataFlowForTarget(NLCPGNode target)
        {
            return _dataFlowByTarget.TryGetValue(target, out var ordinals) ? ordinals : NoOrdinals;
        }

        internal bool TryGetArgumentsForMethod(string methodSymbolKey, out int[] ordinals)
        {
            return _argumentsByMethod.TryGetValue(methodSymbolKey, out ordinals!);
        }

        internal bool TryGetReturnsForMethod(string methodSymbolKey, out int[] ordinals)
        {
            return _returnsByMethod.TryGetValue(methodSymbolKey, out ordinals!);
        }

        // 两遍只读扫描：先精确计数，再精确分配与填充。
        //
        // 保留条件与旧实现逐字相同（CallTargets 只进第一类桶；DataFlow 进按目标桶，
        // 目标为方法参数/返回且能解析方法归属时再进对应方法桶），故桶成员集与顺序不变。
        internal static InterproceduralEdgeSnapshot Create(
          IEnumerable<NLCPGGraph.PendingEdge> edges,
          IReadOnlyDictionary<NLCPGNode, string> methodOwnerSymbolKeysByBoundaryNode)
        {
            // ── 第 1 遍：只计数，不分配池，也不分配任何桶数组 ──────────────────────
            var callTargetCounts = new Dictionary<NLCPGNode, int>();
            var dataFlowCounts = new Dictionary<NLCPGNode, int>();
            var argumentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var returnCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var retainedEdgeCount = 0;
            foreach (var edge in edges)
            {
                if (edge.Kind == NLCPGEdgeKind.CallTargets)
                {
                    Increment(callTargetCounts, edge.SourceNode);
                    retainedEdgeCount += 1;
                    continue;
                }

                if (edge.Kind != NLCPGEdgeKind.DataFlow)
                {
                    continue;
                }

                Increment(dataFlowCounts, edge.TargetNode);
                retainedEdgeCount += 1;
                if (methodOwnerSymbolKeysByBoundaryNode.TryGetValue(
                  edge.TargetNode,
                  out var countingMethodKey))
                {
                    if (edge.TargetNode.Kind == NLCPGNodeKind.MethodParameter)
                    {
                        Increment(argumentCounts, countingMethodKey);
                    }
                    else if (edge.TargetNode.Kind == NLCPGNodeKind.MethodReturn)
                    {
                        Increment(returnCounts, countingMethodKey);
                    }
                }
            }

            // 精确分配：池恰好 U 个元素，每个桶数组恰好其计数长度。
            var pool = new NLCPGGraph.PendingEdge[retainedEdgeCount];
            var callTargetsBySource = AllocateBuckets(callTargetCounts);
            var dataFlowByTarget = AllocateBuckets(dataFlowCounts);
            var argumentsByMethod = AllocateBuckets(argumentCounts);
            var returnsByMethod = AllocateBuckets(returnCounts);

            // ── 第 2 遍：再次枚举【同一未改变的边序列】，按原顺序编号并填充 ────────
            // 本段不调用 AddEdge/AddNode（发布在索引构建之后），故枚举期间序列不被修改。
            // 惰性枚举：不物化 PendingEdge[]（实测全量物化在 71 s 快照中分配 641.4 MiB）。
            var writeCursor = new Dictionary<NLCPGNode, int>(callTargetsBySource.Count);
            foreach (var key in callTargetsBySource.Keys)
            {
                writeCursor[key] = 0;
            }

            var targetCursor = new Dictionary<NLCPGNode, int>(dataFlowByTarget.Count);
            foreach (var key in dataFlowByTarget.Keys)
            {
                targetCursor[key] = 0;
            }

            var argumentCursor = new Dictionary<string, int>(argumentsByMethod.Count, StringComparer.Ordinal);
            foreach (var key in argumentsByMethod.Keys)
            {
                argumentCursor[key] = 0;
            }

            var returnCursor = new Dictionary<string, int>(returnsByMethod.Count, StringComparer.Ordinal);
            foreach (var key in returnsByMethod.Keys)
            {
                returnCursor[key] = 0;
            }

            var edgeOrdinal = 0;
            foreach (var edge in edges)
            {
                if (edge.Kind == NLCPGEdgeKind.CallTargets)
                {
                    pool[edgeOrdinal] = edge;
                    Append(callTargetsBySource, writeCursor, edge.SourceNode, edgeOrdinal);
                    edgeOrdinal += 1;
                    continue;
                }

                if (edge.Kind != NLCPGEdgeKind.DataFlow)
                {
                    continue;
                }

                pool[edgeOrdinal] = edge;
                Append(dataFlowByTarget, targetCursor, edge.TargetNode, edgeOrdinal);
                if (methodOwnerSymbolKeysByBoundaryNode.TryGetValue(
                  edge.TargetNode,
                  out var methodSymbolKey))
                {
                    if (edge.TargetNode.Kind == NLCPGNodeKind.MethodParameter)
                    {
                        Append(argumentsByMethod, argumentCursor, methodSymbolKey, edgeOrdinal);
                    }
                    else if (edge.TargetNode.Kind == NLCPGNodeKind.MethodReturn)
                    {
                        Append(returnsByMethod, returnCursor, methodSymbolKey, edgeOrdinal);
                    }
                }

                edgeOrdinal += 1;
            }

            // 两遍必须看到同一序列；不一致说明填充期有写入者，索引与池已不可信。
            if (edgeOrdinal != retainedEdgeCount)
            {
                throw new InvalidOperationException(
                  $"The counting and fill passes saw different retained edge counts "
                  + $"({retainedEdgeCount} vs {edgeOrdinal}); the pending edge sequence changed mid-pass.");
            }

            // 每个桶的写游标必须恰等于其精确长度：漏填/多填即为两遍边序不一致。
            VerifyFilled(callTargetsBySource, writeCursor, nameof(callTargetsBySource));
            VerifyFilled(dataFlowByTarget, targetCursor, nameof(dataFlowByTarget));
            VerifyFilled(argumentsByMethod, argumentCursor, nameof(argumentsByMethod));
            VerifyFilled(returnsByMethod, returnCursor, nameof(returnsByMethod));

            // 计数/游标字典到此不再需要：本方法返回后只剩池与四个桶。
            return new InterproceduralEdgeSnapshot(
              pool,
              callTargetsBySource,
              dataFlowByTarget,
              argumentsByMethod,
              returnsByMethod);
        }

        private static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key)
          where TKey : notnull
        {
            counts.TryGetValue(key, out var existing);
            counts[key] = existing + 1;
        }

        private static Dictionary<TKey, int[]> AllocateBuckets<TKey>(Dictionary<TKey, int> counts)
          where TKey : notnull
        {
            var buckets = new Dictionary<TKey, int[]>(counts.Count);
            foreach (var pair in counts)
            {
                buckets[pair.Key] = new int[pair.Value];
            }

            return buckets;
        }

        private static void Append<TKey>(
          Dictionary<TKey, int[]> buckets,
          Dictionary<TKey, int> cursors,
          TKey key,
          int edgeOrdinal)
          where TKey : notnull
        {
            var slot = cursors[key];
            buckets[key][slot] = edgeOrdinal;
            cursors[key] = slot + 1;
        }

        private static void VerifyFilled<TKey>(
          Dictionary<TKey, int[]> buckets,
          Dictionary<TKey, int> cursors,
          string name)
          where TKey : notnull
        {
            foreach (var pair in buckets)
            {
                if (cursors[pair.Key] != pair.Value.Length)
                {
                    throw new InvalidOperationException(
                      $"The '{name}' interprocedural edge index was not filled consistently: the fill "
                      + "pass saw a different number of retained edges than the counting pass.");
                }
            }
        }

        // 四个桶的成员条目总数 L（每个成员一个 int 槽位）。
        // 这是本表示的结构事实：L 可以大于 PoolCount —— 同一条 DataFlow 边同时命中
        // 按目标桶与某个方法桶，差额正是"单池共享"省下的完整载荷副本数。
        internal int BucketSlotCount()
        {
            var slots = 0;
            foreach (var length in BucketLengths())
            {
                slots += length;
            }

            return slots;
        }

        // 每个桶的成员数（不含键）。仅供结构断言度量"迁移前四个 List 的容量形态"，
        // 不参与任何生产路径。
        internal IEnumerable<int> BucketLengths()
        {
            foreach (var bucket in EnumerateBuckets())
            {
                yield return bucket.Length;
            }
        }

        private IEnumerable<int[]> EnumerateBuckets()
        {
            foreach (var bucket in _callTargetsBySource.Values)
            {
                yield return bucket;
            }

            foreach (var bucket in _dataFlowByTarget.Values)
            {
                yield return bucket;
            }

            foreach (var bucket in _argumentsByMethod.Values)
            {
                yield return bucket;
            }

            foreach (var bucket in _returnsByMethod.Values)
            {
                yield return bucket;
            }
        }
    }

    internal void RunInterproceduralDataFlowPass(NLCPGBuildContext context)
    {
        // D1：**逐文档**执行——本阶段的边必须落在各文件自己的图里。
        //   单文件时 `context.Documents` 恒为「自身」一项 ⇒ 与改造前**逐字相同**。
        foreach (var document in context.Documents)
        {
            RunInterproceduralDataFlowPassForDocument(document);
        }
    }

    private void RunInterproceduralDataFlowPassForDocument(NLCPGBuildContext context)
    {
        var graph = context.Graph;
        // CallGraphPass and DataFlowPass have completed their reducers before this
        // stage. From this point on, bridge planning reads a frozen cross-fragment
        // snapshot and only the final publisher mutates the shared graph.
        _interproceduralBarrierCompleted = true;
        // ── M2：一份完整边快照池 + 四个只存 int 序号的索引 ─────────────────────────
        // 旧实现在这里维护四个 Dictionary<TKey, List<PendingEdge>>：同一条边若同时命中
        // DataFlowByTarget 与某个方法桶，就把整条 PendingEdge（实测 272 B，内嵌两个
        // NLCPGNode）完整复制两份。实测（Terraria NPC.cs，2,147,280 字符，DOP=1）：
        //   W=272 B、U=46,251、L=54,691、Σ旧容量=160,744 ⇒ 旧主载荷 41.70 MiB。
        //   L/U = 1.182 ⇒ 确有多桶共享，不是同义反复。
        //
        // 新表示：池里每条保留边只保存一次原值，四个索引只保存池内序号（4 B/成员）。
        // 实测同一夹具下新主载荷 12.98 MiB（池 12.00 + 桶 0.98，含 33,891 个 int[] 的
        // 24 B 对象头），降幅 68.87%。
        //
        // ⚠ 池的 ordinal 是【本 pass 内的边序号】，不是 NodeId、图 node ordinal 或 metadata id。
        // ⚠ 不做值相等/身份相等合并，也不对池额外 Distinct：每条被选中的源边保存一次原值。
        // ⚠ 池保存的是扫描当时的完整 PendingEdge 值快照（readonly record struct，按值复制），
        //   发布阶段【不回读图节点】，故图内节点之后被 MergeNode 替换也不影响本快照。
        //   （契约见 InterproceduralEdgeIndexSnapshotTests 的完整载荷快照用例。）
        // ⚠ 池与四个索引同属一个对象，最后一次窗口发布后一起离开作用域，不进入常驻字段。
        var edgeSnapshot = InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          _methodOwnerSymbolKeysByBoundaryNode);
        // 逐调用点流式处理：只保留【当前调用点】的计划，避免把全部调用点的计划累积成
        // 一个百万级 List 后再做一次全局 Distinct() + 8 级 OrderBy + ToArray 物化。
        // 实测该链在 71 s 快照中同时存活 6 个数组共 755.3 MiB（占当时存活堆 67.75%）。
        var nodeSortKeys = new Dictionary<NLCPGNode, string>();
        var summaryBudget = new FlowSummaryBudget(_options.EffectiveFlowSummaryOptions);
        var recordedReturnMethods = new HashSet<string>(StringComparer.Ordinal);
        var options = _options.EffectiveInterproceduralDataFlowOptions;
        var methodBoundaryNodes = graph.Nodes
          .Where(node => node.Kind is NLCPGNodeKind.MethodParameter or NLCPGNodeKind.MethodReturn)
          .ToArray();
        var internalMethodSymbolKeys = methodBoundaryNodes
          .Select(node => _methodOwnerSymbolKeysByBoundaryNode.TryGetValue(node, out var key) ? key : null)
          .Where(key => key is not null)
          .Select(key => key!)
          .ToHashSet(StringComparer.Ordinal);

        // ── N2-b 阶段 ①：节点排序键的【去重 + 并行】预计算 ─────────────────────────
        // NodeSortKey 是 (Kind, FullName, Name, FilePath, SpanStart, SpanEnd) 的纯函数插值，
        // 与调用点无关。原先是"每调用点分组内按需计算、分组一换缓存即 Clear"，于是被多个
        // 调用点共享的节点（被调方边界节点尤其典型）会被反复重新插值。
        //
        // 这里改为：先按节点去重一次（实测 1,599,506 节点 → 62,251 个唯一键节点），
        // 再把插值放到多核上，循环内因此只剩字典查找。
        // 注：实测本段本身只占发布段约 0.2%（n2b5-PAR.log），故它【不是】主要收益来源，
        // 主要收益来自下面②③的窗口化并行；保留本段是因为它成本极低、又为②提供了只读缓存。
        //
        // 等价性论证：
        //   · 键值只由节点与图内容决定，不依赖计算时机与计算者 ⇒ 分组内比较器结果逐位不变
        //     ⇒ 组内排序结果不变 ⇒ AddEdge 调用的序列逐位不变 ⇒ 快照指纹不变。
        //   · 本段整体位于任何 AddEdge 之前，且只读：Resolve* 走 StringInterner.TryResolve
        //     （内部加锁、且只增不改），NLCPGNode 为 readonly record struct ⇒ 并发读安全。
        //   · 覆盖集是计划节点的【超集】（4 个索引的全部端点），故循环内不可能漏建。
        {
            // M2：覆盖集改为遍历【池】一次，而不是遍历四个索引各一次。
            // 旧实现按四个 Dictionary.Values 遍历，同一节点最多被重复访问 4 次；
            // 池里每条保留边恰好出现一次，故覆盖集仍严格等于四个索引端点的并集（超集安全）。
            var keyedNodeSet = new HashSet<NLCPGNode>();
            foreach (var edge in edgeSnapshot.Pool)
            {
                keyedNodeSet.Add(edge.SourceNode);
                keyedNodeSet.Add(edge.TargetNode);
            }

            var keyedNodes = new NLCPGNode[keyedNodeSet.Count];
            keyedNodeSet.CopyTo(keyedNodes);
            keyedNodeSet.Clear();

            var computedKeys = new string[keyedNodes.Length];
            var keyDop = Math.Max(1, _options.EffectiveMaxDegreeOfParallelism);
            if (keyDop <= 1)
            {
                for (var index = 0; index < keyedNodes.Length; index += 1)
                {
                    computedKeys[index] = NodeSortKey(graph, keyedNodes[index]);
                }
            }
            else
            {
                Parallel.For(
                  0,
                  keyedNodes.Length,
                  new ParallelOptions { MaxDegreeOfParallelism = keyDop },
                  index => computedKeys[index] = NodeSortKey(graph, keyedNodes[index]));
            }

            // 同一节点在 4 个索引间可能重复出现，去重后此处键唯一；重复写入只可能是同值。
            for (var index = 0; index < keyedNodes.Length; index += 1)
            {
                nodeSortKeys[keyedNodes[index]] = computedKeys[index];
            }

            computedKeys = null!;
        }

        var orderedCallSites = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.CallSite)
          .OrderBy(node => graph.ResolveFullName(node), StringComparer.Ordinal)
          .ThenBy(node => node.SpanStart)
          // 调用点自身不在下面的去重集里（该集只覆盖 4 个索引的端点），且仅 2,769 个，
          // 故此处直接插值，不走全局缓存——避免为省这点开销扩大集合。
          .ThenBy(node => NodeSortKey(graph, node), StringComparer.Ordinal)
          .ToArray();
        // ── N2-b：窗口化并行发布 ──────────────────────────────────────────────────
        // 实测把任务卡所称的"去重+组内排序（49.4%）"再拆三段后（n2b-NEW-probe.log）：
        //   ① 排序键插值 0.2%   ② 构造排序行 15.0%   ③ List.Sort 33.2%   ④ AddEdge 51.6%
        // 即：真正的熵源是 ②③（48.4%），而它们是【只读纯计算】——只读计划列表与全局键缓存，
        // 不碰共享图。故这里把 ②③ 按窗口并行，④ 仍在窗口内按 callSiteOrder 严格串行。
        //
        // 等价性论证：
        //   · 计划【构造】保持串行：它含 recordedReturnMethods 这一跨迭代顺序相关门控
        //     （任务卡列为"已知阻塞"），故不可并行。
        //   · ②③ 是 (计划列表, 键缓存) 的纯函数，二者在此阶段只读 ⇒ 行内容与组内次序不变。
        //     Dictionary 并发【只读】是安全的；键缓存的兜底分支也只插值不写入。
        //   · 写图顺序 = 窗口内各槽按 callSiteOrder 升序、槽内为排序后次序，与串行版逐位相同
        //     ⇒ AddEdge 调用序列不变 ⇒ 快照指纹不变。
        //   · 窗口双上界（组数 + 行数）：实测单组最大 5,878 行，故按行数封顶可确定性地
        //     限制峰值内存，避免把 3,954,144 行（≈1.7 GB）同时存活。
        const int ParallelPublishMaxGroupsPerWindow = WindowMaxGroupsPerPublish;
        const int ParallelPublishMaxRowsPerWindow = WindowMaxRowsPerPublish;
        // 窗口持有【组】而非裸计划列表：调用点元数据（CallSiteNode / 原调用点顺序）由组头持有，
        // 计划本身缩为「池内序号 + 实参序」这一个 8 B 惰性载体。见 InterproceduralDataFlowPlanGroup
        // 的类型注释与 InterproceduralPlanRef 的"延迟物化"说明。
        var windowGroups = new InterproceduralDataFlowPlanGroup[ParallelPublishMaxGroupsPerWindow];
        var windowRows = new List<PlanSortRow>[ParallelPublishMaxGroupsPerWindow];
        for (var slot = 0; slot < ParallelPublishMaxGroupsPerWindow; slot += 1)
        {
            windowRows[slot] = new List<PlanSortRow>();
        }

        var windowCount = 0;
        var windowRowCount = 0;
        var publishDop = Math.Max(1, _options.EffectiveMaxDegreeOfParallelism);
        var capacityLedger = new InterproceduralPlanCapacityLedgerBuilder();

        for (var callSiteOrder = 0; callSiteOrder < orderedCallSites.Length; callSiteOrder += 1)
        {
            var callSite = orderedCallSites[callSiteOrder];
            // M2：索引只给 int 序号，端点节点一律经池回读。
            var callTargetOrdinals = edgeSnapshot.CallTargetsForSource(callSite);
            var targets = callTargetOrdinals.Length == 0
              ? Array.Empty<NLCPGNode>()
              : callTargetOrdinals
              .Select(ordinal => edgeSnapshot.Edge(ordinal).TargetNode)
              .Distinct()
              .OrderBy(target => graph.ResolveFullName(target), StringComparer.Ordinal)
              .ThenBy(target => nodeSortKeys[target], StringComparer.Ordinal)
              .ToArray();
            if (targets.Length == 0)
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                summaryBudget.RecordCut("UnresolvedTarget");
                continue;
            }

            if (targets.Length > options.MaxCallTargetsPerSite)
            {
                summaryBudget.RecordCut("AmbiguousTarget");
                continue;
            }

            var targetMethodNode = targets[0];
            if (!TryGetSymbolKey(targetMethodNode, out var targetMethodSymbolKey))
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                summaryBudget.RecordCut("UnresolvedTarget");
                continue;
            }

            var hasInternalBoundary = internalMethodSymbolKeys.Contains(targetMethodSymbolKey);
            if (!hasInternalBoundary)
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                summaryBudget.RecordCut("ExternalTarget");
                continue;
            }

            // M2：经池回读端点；桶里只放 int，缺失键用共享空数组。
            var argumentOrdinals = edgeSnapshot.TryGetArgumentsForMethod(
              targetMethodSymbolKey,
              out var indexedArgumentOrdinals)
              ? indexedArgumentOrdinals
              : Array.Empty<int>();
            var returnToCallOrdinals = edgeSnapshot.DataFlowForTarget(callSite);
            // 预分配容量：下面三段的上界都可由 Count 直接算出，故无需依赖 List 的倍增增长。
            // 【⑥】计划载体已缩为「池内序号 + 实参序」= 2 × int = 8 B
            // （方案 B 时为 216 B，更早为 4 个 NLCPGNode + 2 个 int ≈ 428 B）。
            // 按当前实测最大单组 5,050 条估算，单份数组约 40 KB，已落回 SOH；
            // 但上界预分配照旧保留——它消除的是【倍增期的多份中间数组】，与是否落 LOH 无关。
            // 实测（60 s trace）本链的 List 增长分配达 1,247.59 MiB，占该窗口总分配的 11.3%：
            //   List<…>.set_Capacity  696.19 MiB / 3,745 ticks
            //   List<…>.AddWithResize 551.40 MiB / 1,848 ticks
            // 事先给出容量后 Add/AddRange 不再扩容，上述两项归零。
            // 该上界安全：第一段 = argumentOrdinals.Length；第二段经 Where 过滤只减不增；
            // 第三段 = indexedReturnOrdinals.Length。此处 TryGet 无副作用，
            // 提前求值不改变下面 recordedReturnMethods 的门控语义。
            var hasReturnDataFlowEdges = edgeSnapshot.TryGetReturnsForMethod(
              targetMethodSymbolKey,
              out var indexedReturnOrdinals);
            var boundaryEdgeBudget = options.MaxBoundaryEdgesPerMethod;
            var planUpperBound = argumentOrdinals.Length
              + returnToCallOrdinals.Length
              + (hasReturnDataFlowEdges ? indexedReturnOrdinals!.Length : 0);
            // 【M1 第 3 节第 1 点】正预算走【前缀收集】：容量直接取 min(原上界, B)，
            // 逐条生成时只把前 B 条加入列表，故从不需要"先全建再截断"的那份超额载荷。
            // 非正预算是【兼容分支】，保留原「按上界全建 + RemoveRange 截断」边界，
            // 以免把既有 ArgumentOutOfRangeException（实测：负值在 RemoveRange、0 在 plans[0]）
            // 静默变成"跳过"。这里不维护两套正常输入算法，只区分合法/非法输入。
            var callSitePlans = InterproceduralPlanBuffer.Create(
              boundaryEdgeBudget > 0 ? Math.Min(planUpperBound, boundaryEdgeBudget) : planUpperBound);
            // 初始容量必须在此刻读取：它就是"前缀收集 vs 先全建再截断"的判别量
            // （旧实现截断后也 TrimExcess，最终容量同样回到 B，看不出差别）。
            // 注意它取自 buffer 固化的请求值，不是 Capacity —— 池化的桶对齐会放大 Capacity。
            var planInitialCapacity = callSitePlans.InitialCapacity;
            var planOverflowed = false;
            foreach (var ordinal in argumentOrdinals)
            {
                if (boundaryEdgeBudget > 0 && callSitePlans.Count >= boundaryEdgeBudget)
                {
                    planOverflowed = true;
                    continue;
                }

                var edge = edgeSnapshot.Edge(ordinal);
                callSitePlans.Add(new InterproceduralPlanRef(
                  ordinal,
                  ParseArgumentOrdinal(edge.TargetNode)));
            }

            foreach (var ordinal in returnToCallOrdinals)
            {
                var edge = edgeSnapshot.Edge(ordinal);
                if (edge.SourceNode.Kind != NLCPGNodeKind.MethodReturn ||
                  !edge.TargetNode.Equals(callSite) ||
                  !IsMethodBoundaryNode(edge.SourceNode, targetMethodSymbolKey))
                {
                    continue;
                }

                if (boundaryEdgeBudget > 0 && callSitePlans.Count >= boundaryEdgeBudget)
                {
                    planOverflowed = true;
                    continue;
                }

                callSitePlans.Add(new InterproceduralPlanRef(ordinal));
            }

            // return-method 门控【不受预算影响】：即使本组已溢出，recordedReturnMethods
            // 仍必须按原串行位置更新，否则后续调用点会重复产出 ReturnToMethodReturn 桥。
            if (recordedReturnMethods.Add(graph.ResolveFullName(targetMethodNode) ?? string.Empty))
            {
                if (hasReturnDataFlowEdges)
                {
                    foreach (var ordinal in indexedReturnOrdinals!)
                    {
                        if (boundaryEdgeBudget > 0 && callSitePlans.Count >= boundaryEdgeBudget)
                        {
                            planOverflowed = true;
                            continue;
                        }

                        callSitePlans.Add(new InterproceduralPlanRef(ordinal));
                    }
                }
            }

            if (callSitePlans.Count == 0)
            {
                // 【⑥】本组不进入窗口。⑤ 的池化已退役，此处不再需要配对的 RecordRent/Return：
                // 精确分配的数组没有"漏归还"这个失败模式，交给 GC 即可。
                summaryBudget.RecordCut("MissingIntraFacts");
                continue;
            }

            if (boundaryEdgeBudget > 0)
            {
                capacityLedger.ObserveGroup(
                  callSitePlans.Count,
                  callSitePlans.Capacity,
                  planInitialCapacity);

                // 超额在生成时即已省略，这里只补记一次原有的截断事件（名称与口径不变）。
                if (planOverflowed)
                {
                    summaryBudget.RecordCut("BoundaryEdgeBudget");
                }

                // 未溢出但上界严重偏大的组：保留 min(上界, B) 的 slack 而不复制整组去省它。
                // 但 slack 必须可观测，否则"取消了 TrimExcess"就变成没有账的空承诺。
                // ⑥ 起元素宽为 8 B（精确分配，无桶对齐浪费），故这份 slack 现在是精确值。
                capacityLedger.PlanCapacitySlackBytes +=
                  (callSitePlans.Capacity - callSitePlans.Count) *
                  System.Runtime.CompilerServices.Unsafe.SizeOf<InterproceduralPlanRef>();
                if (callSitePlans.Capacity > callSitePlans.Count)
                {
                    capacityLedger.PlanCapacitySlackGroups += 1;
                }
            }
            else if (callSitePlans.Count > boundaryEdgeBudget)
            {
                // 非正预算兼容分支：逐位保留原「全建 → RemoveRange」边界。
                // 此处【不】改成前缀收集，也不改 TrimExcess 之外的任何顺序，
                // 以免把既有异常变成静默跳过。
                summaryBudget.RecordCut("BoundaryEdgeBudget");
                callSitePlans.RemoveRange(
                  boundaryEdgeBudget,
                  callSitePlans.Count - boundaryEdgeBudget);
                callSitePlans.TrimExcess();
                capacityLedger.PlanTrimCount += 1;
            }

            // 计划数组在构造后即不再被本迭代修改（上面只有本迭代自己的追加/截断），
            // 故可安全交给窗口并行消费；窗口满则先冲刷。组头在此密封，之后不得追加。
            windowGroups[windowCount] = new InterproceduralDataFlowPlanGroup(
              callSite,
              callSiteOrder,
              callSitePlans);
            windowRowCount += callSitePlans.Count;
            windowCount += 1;

            if (windowCount == ParallelPublishMaxGroupsPerWindow ||
              windowRowCount >= ParallelPublishMaxRowsPerWindow)
            {
                FlushParallelPublishWindow(
                  graph,
                  edgeSnapshot,
                  windowGroups,
                  windowRows,
                  windowCount,
                  nodeSortKeys,
                  publishDop,
                  capacityLedger);
                windowCount = 0;
                windowRowCount = 0;
            }
        }

        if (windowCount > 0)
        {
            FlushParallelPublishWindow(
              graph,
              edgeSnapshot,
              windowGroups,
              windowRows,
              windowCount,
              nodeSortKeys,
              publishDop,
              capacityLedger);
        }

        LastFlowSummaryMetrics = summaryBudget.ToMetrics();
        LastInterproceduralPlanCapacity = capacityLedger.ToLedger();

        // G0-P R-2：该阶段不产 fragment，直接向图发布桥接边；回报记账结果。
        // 用当前图规模作为产出规模代理（本阶段只增边，节点数由前序阶段决定）。
        RecordInterproceduralStageResult(graph.Nodes.Count, graph.Edges.Count);
    }

    // 冲刷一个窗口：② 构造排序行 + ③ List.Sort【并行】，④ AddEdge 写图【串行】。
    //
    // 与旧的全局 `plans.Distinct().OrderBy(8 级).ToArray()` 逐位一致的论证：
    // 1. 旧排序的【首键】StableCallSiteOrder 就是调用点循环下标，且 LINQ OrderBy 是稳定排序，
    //    故全局结果 = 各调用点分组结果按 callSiteOrder 升序拼接；下面按窗口内 slot 升序
    //    逐组发布，组间/组内次序都与串行版相同。
    // 2. 记录含 StableCallSiteOrder，跨组计划永不相等 ⇒ 全局 Distinct 等价于组内去重；
    //    且 HashSet 首次插入即保留、后续重复丢弃，与 LINQ Distinct 的"保留首次出现序"一致。
    // 3. 组内 CallSiteNode / TargetMethodNode 恒定 ⇒ 旧排序第 3、4 键（NodeSortKey(CallSite)、
    //    NodeSortKey(TargetMethod)）在组内恒为平局，是空操作；BridgeKind 出现在第 2 键与第 6 键，
    //    第 6 键必为平局。故组内有效键恰好是
    //      BridgeKind -> ArgumentOrdinal -> NodeSortKey(Source) -> NodeSortKey(Target)。
    //    下面只算这两个 NodeSortKey（取自阶段 ① 的只读全局缓存）。
    // 4. NLCPGGraph.AddEdge 只做 append 到序数键集，不读回中间态；AddNode 按稳定锚点幂等合并，
    //    故在循环中提前发布不会改变后续迭代读到的索引内容。
    // 5. 最终边序由 NLCPGGraphIndex.Create 的确定性排序决定（SourceNodeId/Kind/TargetNodeId/...），
    //    与插入序无关 ⇒ 快照指纹不受本重构影响。
    //
    // 为什么②③可并行、④必须串行：
    //   · ②③ 是 (计划列表, 只读键缓存) 的纯函数，结果写进各自 slot ⇒ 无数据竞态、无顺序依赖，
    //     并行只改变"计算发生的时刻/线程"，不改变任何行的值或组内次序。
    //   · ④ 写共享图，必须串行；且按 slot 升序执行以复刻串行版的发布次序。
    private static void FlushParallelPublishWindow(
      NLCPGGraph graph,
      InterproceduralEdgeSnapshot edgeSnapshot,
      InterproceduralDataFlowPlanGroup[] windowGroups,
      List<PlanSortRow>[] windowRows,
      int windowCount,
      Dictionary<NLCPGNode, string> nodeSortKeys,
      int dop,
      InterproceduralPlanCapacityLedgerBuilder capacityLedger)
    {
        capacityLedger.FlushCount += 1;
        var windowRowsObserved = 0L;
        for (var observedSlot = 0; observedSlot < windowCount; observedSlot += 1)
        {
            windowRowsObserved += windowGroups[observedSlot].Count;
        }

        capacityLedger.ObserveWindow(windowCount, windowRowsObserved);
        if (dop <= 1 || windowCount == 1)
        {
            for (var slot = 0; slot < windowCount; slot += 1)
            {
                BuildAndSortPlanRows(
                  graph,
                  edgeSnapshot,
                  windowGroups[slot].Plans,
                  windowRows[slot],
                  nodeSortKeys);
            }
        }
        else
        {
            // 每个 slot 写各自的 List ⇒ 无需锁、无数据竞争。
            Parallel.For(
              0,
              windowCount,
              new ParallelOptions { MaxDegreeOfParallelism = dop },
              slot => BuildAndSortPlanRows(
                graph,
                edgeSnapshot,
                windowGroups[slot].Plans,
                windowRows[slot],
                nodeSortKeys));
        }

        // ④ 串行写图：严格按 slot 升序（= callSiteOrder 升序），槽内按已排定的次序。
        for (var slot = 0; slot < windowCount; slot += 1)
        {
            var rows = windowRows[slot];
            var group = windowGroups[slot];
            windowGroups[slot] = default;

            // 【方案 B】调用点元数据在组头，一组算一次即可（原先是逐行读取计划里的 CallSiteNode，
            // 再对 3,954,144 行各算一次上下文；每行含 2 次 Resolve 与 1 次 record 构造）。
            var plans = group.Plans;

            // 【非正预算兼容分支】复刻方案 B 移除掉的那次读取。
            // 原发布器在这里用 `plans[0].CallSiteNode` 取调用点上下文，因此当某组被
            // `MaxBoundaryEdgesPerMethod` 裁成 0 条时（实测：预算 0），它在【发布阶段】
            // 抛 ArgumentOutOfRangeException("index")。
            // 方案 B 把调用点移到组头后这次读取不复存在 ⇒ 若不显式复刻，预算 0 会从
            // "抛异常"静默变成"成功且少发边"，那是一处无人察觉的契约变更。
            // 冻结读数见 Build/MemoryOptimization/M1/run-20260925-01/BASELINE.md。
            // 可达性：构造段 `Count == 0 ⇒ continue` 已挡住空组，故只有非正预算截断
            // 才会让空组进入窗口——这正是要复刻的那条路径。
            if (plans.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                  "index",
                  "Index was out of range. Must be non-negative and less than the size of the collection.");
            }

            var callSiteContext = BuildPendingNodeCallSiteContext(graph, group.CallSiteNode);
            foreach (var row in rows)
            {
                // 方案 A + ⑥：排序行只保存组内计划下标，发布时按下标回读【惰性载体】，
                // 再由载体里的池内序号向池现取端点与桥种类。
                // 下标绑定 callSitePlans 的原始插入序；该列表在整组发布结束前不重排、不追加、不跨组复用。
                var edge = edgeSnapshot.Edge(plans[row.PlanIndex].PoolOrdinal);
                graph.AddEdge(
                  edge.SourceNode,
                  edge.TargetNode,
                  NLCPGEdgeKind.InterproceduralDataFlow,
                  NLCPGEdgeLabel.ForInterproceduralBridge(BridgeKindOf(edge)),
                  callSiteContext: callSiteContext);
            }

            // 【M1 第 3 节第 2 点】该组已发布完毕 ⇒ 清空排序行以释放 SourceKey/TargetKey
            // 两个字符串引用。必须在此处（串行发布段内）做，不能在 worker 里做：
            // 此刻所有 worker 已结束，不会再有人读这个槽。
            // 不在此处 TrimExcess —— 容量留着复用，是否保留由下方"全部 64 槽合计预算"统一裁决。
            rows.Clear();
            capacityLedger.SortBufferClearedSlots += 1;
            // 【可观测性】紧随 Clear() 之后实测残留行数并累加。
            // 正常路径恒为 0；把上面那行 Clear() 删掉而只留计数器时，该值立刻 > 0，
            // 使「引用真的被释放」成为可观测事实，而不是"计数器被加过"。
            // 这是本项唯一能覆盖总索引 §4「强引用可达性」轴的量：windowRows 是
            // FlushParallelPublishWindow 的形参，构建结束后测试无法直接观察槽内容。
            capacityLedger.SortBufferResidualRows += rows.Count;
        }

        ReclaimIdleSortBufferCapacity(windowRows, capacityLedger);
    }

    // 【M1 第 3 节第 2 点】空闲排序缓冲的【总容量】治理。
    //
    // 治理对象是"发布后仍被 64 个槽扣住、却没有元素使用"的数组容量，不是活跃计划、
    // 更不是进程内存。原状是每槽各自保留历史最大值（大组经过后该槽永久留住那份容量），
    // 于是 64 个槽的保留量之和可以远超任一时刻的实际需求。
    //
    // 规则：合计保留字节超过内部预算时，按容量【从大到小】替换为空列表；
    // 容量相同则按槽号升序——即淘汰次序完全确定，不依赖字典序/线程调度。
    // 超大单组的排序数组发布后一律不保留（它就是超出预算的主因）。
    //
    // 预算取「窗口行数阈值 × 实测行宽」：行数阈值本身已是窗口触发条件，
    // 故此预算与窗口规模同量级，不引入新的公开配置，也不声称约束总进程内存。
    //
    // internal 而非 private：本方法【以真实语料几乎不可达】（窗口在 131072 行就冲刷，
    // 因而各槽合计容量通常都在预算之内），所以它的接线——真的替换掉了那些槽、
    // 真的扣减了账本字节——只能靠合成槽数组直接驱动。若不可直测，
    // "淘汰确实发生"就永远只是注释里的说法。
    internal static void ReclaimIdleSortBufferCapacity(
      List<PlanSortRow>[] windowRows,
      InterproceduralPlanCapacityLedgerBuilder capacityLedger)
    {
        var rowWidth = System.Runtime.CompilerServices.Unsafe.SizeOf<PlanSortRow>();
        var retainedBudgetBytes = (long)WindowMaxRowsPerPublish * rowWidth;
        var capacities = new int[windowRows.Length];
        for (var slot = 0; slot < windowRows.Length; slot += 1)
        {
            capacities[slot] = windowRows[slot].Capacity;
        }

        var retainedBytes = TotalRetainedBytes(capacities, rowWidth);

        // 治理【之前】的读数。必须与治理【之后】的读数分开记：
        // 只记后者的话，"合计 ≤ 预算"这条断言恒真（治理的定义就是把它压到预算内），
        // 那样的护栏对"回收逻辑被整段删掉"完全无感。
        if (retainedBytes > capacityLedger.SortBufferPeakRetainedBytes)
        {
            capacityLedger.SortBufferPeakRetainedBytes = retainedBytes;
        }

        foreach (var slot in SelectSortSlotsToReclaim(capacities, rowWidth, retainedBudgetBytes))
        {
            retainedBytes -= (long)capacities[slot] * rowWidth;
            windowRows[slot] = new List<PlanSortRow>();
            capacityLedger.SortBufferReclaimedSlots += 1;
        }

        capacityLedger.SortBufferRetainedBytes = retainedBytes;
    }

    private static long TotalRetainedBytes(int[] capacities, long rowWidth)
    {
        var total = 0L;
        foreach (var capacity in capacities)
        {
            total += capacity * rowWidth;
        }

        return total;
    }

    // 淘汰决策【纯函数】：给定各槽容量，返回需要丢弃的槽号，按淘汰次序排列。
    //
    // 抽成纯函数是有意的：窗口在 131072 行处就会冲刷，故"当前窗口各槽容量之和"本身
    // 几乎总在预算之内，回收只在跨窗口累积时才触发。也就是说，用真实语料很难稳定地
    // 构造出回收——若把决策埋在 ReclaimIdleSortBufferCapacity 里，它实际上不可测，
    // 于是"确定性淘汰"就退化成一句无人验证的注释。抽出来后可直接用合成容量验证。
    //
    // 确定性：容量降序、同容量按槽号升序 ⇒ 不依赖字典序、哈希序或线程调度。
    internal static int[] SelectSortSlotsToReclaim(
      int[] capacities,
      long rowWidth,
      long retainedBudgetBytes)
    {
        var retainedBytes = TotalRetainedBytes(capacities, rowWidth);
        if (retainedBytes <= retainedBudgetBytes)
        {
            return Array.Empty<int>();
        }

        var order = Enumerable
          .Range(0, capacities.Length)
          .OrderByDescending(slot => capacities[slot])
          .ThenBy(slot => slot)
          .ToArray();

        var reclaimed = new List<int>();
        foreach (var slot in order)
        {
            if (retainedBytes <= retainedBudgetBytes)
            {
                break;
            }

            if (capacities[slot] == 0)
            {
                continue;
            }

            reclaimed.Add(slot);
            retainedBytes -= (long)capacities[slot] * rowWidth;
        }

        return reclaimed.ToArray();
    }

    // ② 构造排序行 + ③ 组内排序（纯函数，可并行）。
    //
    // ⑥ 起计划载体只存池内序号，故本方法必须拿到 <paramref name="edgeSnapshot"/> 才能展开
    // 排序键（端点排序键与桥种类）。它仍是 static：池与键缓存都是只读的，加一个形参
    // 就是本项唯一的跨方法接线。
    private static void BuildAndSortPlanRows(
      NLCPGGraph graph,
      InterproceduralEdgeSnapshot edgeSnapshot,
      InterproceduralPlanBuffer callSitePlans,
      List<PlanSortRow> sortRows,
      Dictionary<NLCPGNode, string> nodeSortKeys)
    {
        sortRows.Clear();
        if (sortRows.Capacity < callSitePlans.Count)
        {
            sortRows.Capacity = callSitePlans.Count;
        }

        for (var planIndex = 0; planIndex < callSitePlans.Count; planIndex += 1)
        {
            var plan = callSitePlans[planIndex];
            // ⑥ 延迟物化：端点是现取的（旧表示在这里读的是计划内嵌的节点副本）。
            // 原先这里先经 `seenPlans`（HashSet<...>）做一次组内去重。
            // 实测 3,954,144 次尝试中丢弃恒为 0，该去重是冗余的，已删除。
            //
            // 冗余是结构性而非语料巧合：真正的去重发生在下游
            // NLCPGGraph.AddEdge -> PendingEdgeBuffer.Add -> HashSet<PendingEdgeKey>，
            // 它以（源序数, 目标序数, 边种类, 元数据 id）为键，比本处按整条计划去重更强
            // （本处的 StableCallSiteOrder 只是把同一调用点的重复项也一并覆盖）。
            // 删除它不改变最终边集：重复计划只会产生重复的 AddEdge 调用，而下游按值去重。
            // 边序亦不受影响：CLR 的 Array.Sort/List.Sort 是【不稳定】排序，故下面仍按
            // sortRows.Count 补齐末键构成严格全序，使组内顺序完全确定，与插入序无关。
            //
            // 方案 A：行里不再内嵌整条计划（若 P0≈428 B，按历史布局约省 408 B/行），
            // 只保存 PlanIndex + 4 个排序键。PlanIndex 即原始插入序，同时充当末键，
            // 故无需再存一份 InsertionOrder——当前没有行过滤和前置去重，两者恒等；
            // 若将来引入行过滤，此恒等关系必须重新审查。
            var edge = edgeSnapshot.Edge(plan.PoolOrdinal);
            sortRows.Add(new PlanSortRow(
              planIndex,
              BridgeKindOf(edge),
              plan.ArgumentOrdinal,
              CachedNodeSortKey(graph, nodeSortKeys, edge.SourceNode),
              CachedNodeSortKey(graph, nodeSortKeys, edge.TargetNode)));
        }

        if (sortRows.Count > 1)
        {
            sortRows.Sort(PlanSortRowComparer.Instance);
        }
    }

    // 取节点的排序键：优先用阶段 ① 建的全局缓存（去重 + 并行插值的结果）。
    // 缓存未命中时按需插值——键是纯函数，值与缓存里的完全相同，故这里只是
    // 防止"覆盖集推理有误"导致崩溃的兜底，不会改变任何比较结果。
    private static string CachedNodeSortKey(
      NLCPGGraph graph,
      Dictionary<NLCPGNode, string> nodeSortKeys,
      NLCPGNode node)
    {
        if (nodeSortKeys.TryGetValue(node, out var cached))
        {
            return cached;
        }

        return NodeSortKey(graph, node);
    }

    // 排序行：预算组内有效的 4 个键，避免在比较器里反复插值 NodeSortKey。
    // 【方案 A】行里不内嵌整条计划，只保存 PlanIndex（组内原始插入序）
    // 与比较所需的 4 个键；发布时经载体里的池内序号回读端点。
    // PlanIndex 同时承担原 InsertionOrder 的角色：List<T>.Sort 不稳定，末键必须构成严格全序，
    // 而当前没有行过滤/前置去重，故组内下标与首次出现序恒等。
    // 行宽随之从「计划宽度 + 5 个键」降为「int + int + int + 2 引用」= 32 B。
    // ⑥ 后计划载体只有 8 B，故「排序行必须窄于计划」这条旧前提已【反转】——
    // 行比载体宽是正常的：行要持两个字符串引用，载体只需两个 int。
    // internal（而非 private）仅为让容量治理的【接线】可被直接测试，见
    // ReclaimIdleSortBufferCapacity 的注释；仍然是非公开嵌套类型。
    internal readonly record struct PlanSortRow(
      int PlanIndex,
      NLCPGInterproceduralBridgeKind BridgeKind,
      int ArgumentOrdinal,
      string SourceKey,
      string TargetKey);

    private sealed class PlanSortRowComparer : IComparer<PlanSortRow>
    {
        internal static PlanSortRowComparer Instance { get; } = new();

        public int Compare(PlanSortRow x, PlanSortRow y)
        {
            // 转 int 后比较：该 enum 未实现 IComparable<T>，直接 CompareTo 会绑定到
            // IComparable.CompareTo(object)，两侧实参各装箱一次（实测 48 B/次）。
            // enum 底层为 int，故该转换与 Enum.CompareTo 的次序逐位等价。
            var result = ((int)x.BridgeKind).CompareTo((int)y.BridgeKind);
            if (result != 0)
            {
                return result;
            }

            result = x.ArgumentOrdinal.CompareTo(y.ArgumentOrdinal);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(x.SourceKey, y.SourceKey);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(x.TargetKey, y.TargetKey);
            if (result != 0)
            {
                return result;
            }

            // 末键用 PlanIndex：它等于该计划的组内原始插入序，复刻 LINQ OrderBy 的稳定排序语义。
            return x.PlanIndex.CompareTo(y.PlanIndex);
        }
    }

    private void RecordWorkBatchPerformanceEvent(CpgWorkBatchPerformanceEvent performanceEvent)
    {
        _workBatchPerformanceEvents.Add(performanceEvent);
        _options.WorkBatchPerformanceEventSink.TryRecord(performanceEvent);
    }

    private void AddExternalSummaryMappings(NLCPGNode callSite, NLCPGGraph graph, FlowSummaryBudget budget)
    {
        if (_options.CallFlowResolver is null)
        {
            budget.BeginCallSite();
            budget.RecordRejected(ResolvedCallFlowStatus.Unknown);
            budget.RecordCut("MissingResolver");
            return;
        }

        var invocation = _callSiteNodesByInvocation
          .FirstOrDefault(pair => pair.Value == callSite).Key;
        if (invocation is null)
        {
            budget.RecordCut("MissingInvocationOperation");
            return;
        }

        var context = BuildPendingNodeCallSiteContext(graph, callSite);
        budget.BeginCallSite();
        var resolvedMappings = _options.CallFlowResolver.ResolveAll(invocation).ToArray();
        if (resolvedMappings.Length == 0)
        {
            budget.RecordRejected(ResolvedCallFlowStatus.Unknown);
            budget.RecordCut("MissingSummary");
            return;
        }

        foreach (var resolved in resolvedMappings.Where(result =>
          !result.IsResolved ||
          result.Mapping is null ||
          result.Mapping.Kind == FlowSummaryMappingKind.Block))
        {
            budget.RecordRejected(resolved);
        }

        foreach (var resolved in resolvedMappings
          .Where(result =>
            result.IsResolved &&
            result.Mapping is not null &&
            result.Mapping.Kind != FlowSummaryMappingKind.Block)
          .OrderBy(result => result.MethodKey.StableKey, StringComparer.Ordinal)
          .ThenBy(result => result.Mapping!.Source.Kind)
          .ThenBy(result => result.Mapping!.Source.ParameterOrdinal)
          .ThenBy(result => result.Mapping!.Target.Kind)
          .ThenBy(result => result.Mapping!.Target.ParameterOrdinal)
          .ThenBy(result => result.Mapping!.Kind)
          .DistinctBy(result => new
          {
              result.MethodKey.StableKey,
              result.Resolution,
              Mapping = result.Mapping!
          }))
        {
            var source = ResolveInvocationEndpointNode(invocation, callSite, resolved.Mapping!.Source, graph);
            var target = ResolveInvocationEndpointNode(invocation, callSite, resolved.Mapping.Target, graph);
            if (source is null || target is null)
            {
                budget.RejectedEndpoints += 1;
                budget.RecordCut("SummaryEndpointUnavailable");
                continue;
            }

            if (!budget.TryConsume(resolved.MethodKey.StableKey))
            {
                continue;
            }

            graph.AddEdge(
              source.Value,
              target.Value,
              NLCPGEdgeKind.InterproceduralDataFlow,
              NLCPGEdgeLabel.ForFlowSummaryBridge(
                NLCPGInterproceduralBridgeKind.SummaryMapping,
                resolved.Resolution,
                resolved.MethodKey.StableKey,
                resolved.Mapping.Source,
                resolved.Mapping.Target),
              callSiteContext: context);
        }
    }

    private NLCPGNode? ResolveInvocationEndpointNode(
      IInvocationOperation invocation,
      NLCPGNode callSite,
      FlowSummaryEndpoint endpoint,
      NLCPGGraph graph)
    {
        if (endpoint.Kind == FlowSummaryEndpointKind.Return)
        {
            return callSite;
        }

        if (endpoint.Kind == FlowSummaryEndpointKind.Receiver)
        {
            return invocation.Instance is null ? null : GetOrCreateOperationNode(invocation.Instance, graph);
        }

        var argument = invocation.Arguments.FirstOrDefault(candidate =>
          candidate.Parameter?.Ordinal == endpoint.ParameterOrdinal);
        if (argument is null || !IsCompatibleArgumentEndpoint(argument, endpoint))
        {
            return null;
        }

        return GetOrCreateOperationNode(argument.Value, graph);
    }

    private static bool IsCompatibleArgumentEndpoint(
      IArgumentOperation argument,
      FlowSummaryEndpoint endpoint)
    {
        if (argument.Parameter is null)
        {
            return false;
        }

        return endpoint.Kind switch
        {
            FlowSummaryEndpointKind.Parameter => argument.Parameter.RefKind == RefKind.None,
            FlowSummaryEndpointKind.RefParameter => argument.Parameter.RefKind == RefKind.Ref,
            FlowSummaryEndpointKind.OutParameter => argument.Parameter.RefKind == RefKind.Out,
            _ => false,
        };
    }

    private bool IsMethodBoundaryNode(NLCPGNode boundaryNode, string targetMethodSymbolKey)
    {
        return _methodOwnerSymbolKeysByBoundaryNode.TryGetValue(boundaryNode, out var boundaryMethodSymbolKey) &&
          string.Equals(boundaryMethodSymbolKey, targetMethodSymbolKey, StringComparison.Ordinal);
    }

    private int ParseArgumentOrdinal(NLCPGNode methodParameterNode)
    {
        return _methodParameterOrdinalsByNode.TryGetValue(methodParameterNode, out var ordinal)
          ? ordinal
          : -1;
    }

    // ⑥ 由池内边导出该计划所属的桥种类——载体里刻意【不存】BridgeKind，这是导出的唯一来源。
    //
    // 判据与 InterproceduralEdgeSnapshot.Create 的入桶条件（段 1/3 只认 MethodParameter /
    // MethodReturn）以及段 2 的过滤条件同源。一个节点只有一种 Kind，故三者互斥，
    // 池内一个序号只属于一个段（每条保留边在池里恰好一份）。
    //
    // ⚠ 这是本项唯一的隐式耦合：它依赖"当前只有这三个产生者"。若将来新增第 4 个产生者
    //   （例如把 SummaryMapping 也走计划路径），下面的 `_` 分支会【静默给出错误结果】。
    //   护栏是 InterproceduralEdgeIndexSnapshotTests 里那条"导出与构造期三段一致"的 Fact。
    // internal（而非 private）仅为让那条护栏能直接驱动本函数 —— 与
    // ReclaimIdleSortBufferCapacity 同一先例；仍然是非公开成员。
    internal static NLCPGInterproceduralBridgeKind BridgeKindOf(NLCPGGraph.PendingEdge edge)
    {
        return edge.TargetNode.Kind switch
        {
            NLCPGNodeKind.MethodParameter =>
              NLCPGInterproceduralBridgeKind.ArgumentToParameter,
            NLCPGNodeKind.MethodReturn =>
              NLCPGInterproceduralBridgeKind.ReturnToMethodReturn,
            _ => NLCPGInterproceduralBridgeKind.MethodReturnToCallResult,
        };
    }

    private static string NodeSortKey(NLCPGGraph graph, NLCPGNode node)
    {
        return $"{node.Kind}|{graph.ResolveFullName(node)}|{graph.ResolveName(node)}|{graph.ResolveFilePath(node)}|{node.SpanStart}|{node.SpanEnd}";
    }

    private static NLCPGCallSiteContext BuildPendingNodeCallSiteContext(NLCPGGraph graph, NLCPGNode node)
    {
        return new NLCPGCallSiteContext(
          graph.ResolveFilePath(node) ?? string.Empty,
          node.SpanStart ?? -1,
          node.SpanEnd ?? -1,
          graph.ResolveFullName(node) ?? graph.ResolveName(node) ?? graph.ResolveDisplayKind(node));
    }

    private bool TryGetSymbolKey(NLCPGNode node, out string symbolKey)
    {
        return _symbolKeysByNode.TryGetValue(node, out symbolKey!);
    }

    private sealed class FlowSummaryBudget
    {
        private readonly NLCPGFlowSummaryOptions _options;
        private readonly Dictionary<string, int> _methodCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _cutReasons = new(StringComparer.Ordinal);
        private int _callSiteCount;

        internal FlowSummaryBudget(NLCPGFlowSummaryOptions options)
        {
            _options = options;
            _options.Validate();
        }

        internal int ResolvedMappings { get; private set; }
        internal int UnknownCalls { get; private set; }
        internal int SignatureMismatches { get; private set; }
        internal int BlockedMappings { get; private set; }
        internal int RejectedEndpoints { get; set; }
        internal int TruncatedMappings { get; private set; }

        internal void RecordCut(string reason)
        {
            _cutReasons[reason] = _cutReasons.TryGetValue(reason, out var count)
                ? count + 1
                : 1;
        }

        internal bool TryConsume(string methodKey)
        {
            var methodCount = _methodCounts.GetValueOrDefault(methodKey);
            if (_callSiteCount >= _options.MaxMappingsPerCallSite ||
                ResolvedMappings >= _options.MaxMappingsPerBuild ||
                methodCount >= _options.MaxMappingsPerMethod)
            {
                TruncatedMappings += 1;
                if (_callSiteCount >= _options.MaxMappingsPerCallSite)
                {
                    RecordCut("SummaryMappingsPerCallSite");
                }

                if (ResolvedMappings >= _options.MaxMappingsPerBuild)
                {
                    RecordCut("SummaryMappingsPerBuild");
                }

                if (methodCount >= _options.MaxMappingsPerMethod)
                {
                    RecordCut("SummaryMappingsPerMethod");
                }

                return false;
            }

            _methodCounts[methodKey] = methodCount + 1;
            _callSiteCount += 1;
            ResolvedMappings += 1;
            return true;
        }

        internal void BeginCallSite()
        {
            _callSiteCount = 0;
        }

        internal void RecordRejected(ResolvedCallFlow resolved)
        {
            var status = resolved.Status;
            if (status == ResolvedCallFlowStatus.Resolved && resolved.Mapping is null)
            {
                status = ResolvedCallFlowStatus.SignatureMismatch;
            }

            if (resolved.Mapping?.Kind == FlowSummaryMappingKind.Block)
            {
                status = ResolvedCallFlowStatus.Blocked;
            }

            RecordRejected(status);
        }

        internal void RecordRejected(ResolvedCallFlowStatus status)
        {
            switch (status)
            {
                case ResolvedCallFlowStatus.Unknown:
                    UnknownCalls += 1;
                    RecordCut("SummaryUnknown");
                    break;
                case ResolvedCallFlowStatus.SignatureMismatch:
                    SignatureMismatches += 1;
                    RecordCut("SummarySignatureMismatch");
                    break;
                case ResolvedCallFlowStatus.Blocked:
                    BlockedMappings += 1;
                    RecordCut("SummaryBlocked");
                    break;
            }
        }

        internal NLCPGFlowSummaryMetrics ToMetrics() => new(
          ResolvedMappings,
          UnknownCalls,
          SignatureMismatches,
          BlockedMappings,
          RejectedEndpoints,
          TruncatedMappings)
        {
            CutReasons = _cutReasons
              .OrderBy(entry => entry.Key, StringComparer.Ordinal)
              .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        };
    }

    private void RunPipeline(IReadOnlyList<INLCPGPass> pipeline, NLCPGBuildContext context)
    {
        foreach (var pass in pipeline)
        {
            pass.Run(this, context);
        }
    }

    private void CompleteOperationBackedSyntaxTypes(NLCPGBuildContext context)
    {
        // ⚠ D1：`_pendingOperationSyntaxTypeNodes` 与 `_syntaxNodes` 都是**构建级**（跨文件共用），
        //   故不能拿驱动 context 的语义模型去 `GetTypeInfo` 别的文件里的语法节点——
        //   `SemanticModel.GetTypeInfo` 要求语法节点属于**该模型自己的语法树**，
        //   否则抛 `ArgumentException: 语法节点不在语法树中`。
        //   必须按语法节点所属文件取回**那个文件**的 context（单文件时恒为自身，行为不变）。
        foreach (var syntax in _pendingOperationSyntaxTypeNodes.ToArray())
        {
            if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode))
            {
                continue;
            }

            var document = context.ResolveDocument(syntax.SyntaxTree.FilePath);
            var typeSymbol = document.SemanticModel.GetTypeInfo(syntax).Type;
            AddTypeEdges(syntaxNode, typeSymbol, document.ResolveGraph(syntax.SyntaxTree.FilePath));
        }

        _pendingOperationSyntaxTypeNodes.Clear();
    }

    private void AddOperationBackedSyntaxTypeEdge(IOperation operation, NLCPGGraph graph)
    {
        if (!_pendingOperationSyntaxTypeNodes.Remove(operation.Syntax) ||
            !_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
        {
            return;
        }

        if (operation.Type is null)
        {
            _pendingOperationSyntaxTypeNodes.Add(operation.Syntax);
            return;
        }

        AddTypeEdges(syntaxNode, operation.Type, graph);
    }

    private void AddControlFlowEdge(NLCPGNode sourceNode, NLCPGNode targetNode, NLCPGEdgeKind edgeKind, NLCPGGraph graph)
    {
        graph.AddEdge(sourceNode, targetNode, edgeKind);
        // ⚠ 判定从「是否为那一张活动图」改为「是否为**任一**活动图」：
        //   多文件下每张图都应缓存自己的 CFG 邻接，否则除一张图外全部静默不缓存。
        if (!_activeBuildGraphs.Contains(graph))
        {
            return;
        }

        if (edgeKind is not (NLCPGEdgeKind.CfgNext or NLCPGEdgeKind.CfgTrue or NLCPGEdgeKind.CfgFalse))
        {
            return;
        }

        AddCfgNeighbor(_cfgSuccessorsByNode, sourceNode, targetNode);
        AddCfgNeighbor(_cfgPredecessorsByNode, targetNode, sourceNode);
    }

    private IReadOnlyCollection<NLCPGNode> GetCachedCfgPredecessors(NLCPGNode node)
    {
        return _cfgPredecessorsByNode.TryGetValue(node, out var predecessors)
          ? predecessors
          : Array.Empty<NLCPGNode>();
    }

    private IReadOnlyCollection<NLCPGNode> GetCachedCfgSuccessors(NLCPGNode node)
    {
        return _cfgSuccessorsByNode.TryGetValue(node, out var successors)
          ? successors
          : Array.Empty<NLCPGNode>();
    }

    private static void AddCfgNeighbor(Dictionary<NLCPGNode, HashSet<NLCPGNode>> neighborsByNode, NLCPGNode node, NLCPGNode neighborNode)
    {
        if (!neighborsByNode.TryGetValue(node, out var neighbors))
        {
            neighbors = new HashSet<NLCPGNode>();
            neighborsByNode[node] = neighbors;
        }

        neighbors.Add(neighborNode);
    }

    private static string PropertyAccessorCallSiteKey(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod)
    {
        return $"{PropertyAccessorCallSitePrefix}:{BuildStableFilePath(propertyReference.Syntax.SyntaxTree.FilePath)}:{propertyReference.Syntax.SpanStart}:{propertyReference.Syntax.Span.End}:{ComposeInvocationMethodFullName(accessorMethod)}";
    }

    private const string PropertyAccessorCallSitePrefix = "callsite-property";

    private static string BuildStableFilePath(string? filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
          ? string.Empty
          : Path.GetFullPath(filePath);
    }

    private static string ComposeOperationPath(IOperation operation)
    {
        var segments = new Stack<int>();
        for (var current = operation; current.Parent is not null; current = current.Parent)
        {
            var childIndex = 0;
            var found = false;
            foreach (var child in current.Parent.ChildOperations)
            {
                if (ReferenceEquals(child, current))
                {
                    found = true;
                    break;
                }

                childIndex += 1;
            }

            segments.Push(found ? childIndex : -1);
        }

        return segments.Count == 0 ? "root" : string.Join(".", segments);
    }


    private NLCPGNode GetOrCreateOperationNode(IOperation operation, NLCPGGraph graph)
    {
        lock (_cacheGate)
        {
            // ⚠ **必须先按图分键**：缓存原先与图无关，一旦某 operation 在 A 图建过节点，
            //   后续为 B 图调用就会返回 A 图的节点，把两张不相交的图交叉链接（静默污染）。
            //   单图构建时外层字典恒一个条目，行为与改造前逐字一致。
            //
            // ⚠ 命中/未命中计数**只记持久图**（`_activeBuildGraphs`），与
            //   `AddControlFlowEdge` 的 CFG 邻接缓存同一判据。理由：
            //   R-3 的 L1 计算层会在**临时图**里算 fragment（ControlFlow/Dominance），
            //   那些节点算完即弃、由 L2 按锚点归并去重；把它们计入会让
            //   `OperationNodeCacheMissCount` 不再等于 `OperationInventoryCount`
            //   （该等式是"每个库存操作在持久图中恰好物化一次"的契约）。
            var isPersistentGraph = _activeBuildGraphs.Contains(graph);
            if (_operationNodesByOperation.TryGetValue(graph, operation, out var cachedNode))
            {
                if (isPersistentGraph)
                {
                    _operationNodeCacheHitCount += 1;
                }

                return cachedNode;
            }

            if (isPersistentGraph)
            {
                _operationNodeCacheMissCount += 1;
            }

            var kind = MapOperationKind(operation);
            var operationNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: kind,
              Name: ResolveOperationName(operation),
              FullName: ResolveOperationFullName(operation),
              Signature: ResolveOperationSignature(operation),
              TypeFullName: ComposeTypeFullName(operation.Type),
              FilePath: operation.Syntax.SyntaxTree.FilePath,
              SpanStart: operation.Syntax.SpanStart,
              SpanEnd: operation.Syntax.Span.End,
              IsImplicit: operation.IsImplicit));
            _operationNodesByOperation.Set(graph, operation, operationNode);
            return operationNode;
        }
    }

    internal IOperation? GetOperationRoot(NLCPGBuildContext context, SyntaxNode bodySyntax)
    {
        lock (_cacheGate)
        {
            if (context.TryGetOperationRoot(bodySyntax, out var cachedOperation))
            {
                _operationRootCacheHitCount += 1;
                return cachedOperation;
            }

            _operationRootCacheMissCount += 1;
            var operation = context.SemanticModel.GetOperation(bodySyntax);
            if (operation is not null)
            {
                context.RegisterOperationRoot(operation);
            }

            return operation;
        }
    }

    private static DataFlowOperationIndex CreateDataFlowOperationIndex(IReadOnlyList<OperationInventoryEntry> operationInventory, IReadOnlyCollection<IOperation> methodRoots)
    {
        var nodesByOperation = new Dictionary<IOperation, NLCPGNode>(
            (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);
        var methodBlockSet = new HashSet<IOperation>(
            methodRoots,
            (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);

        foreach (var entry in operationInventory)
        {
            if (!methodBlockSet.Contains(entry.MethodRoot))
            {
                continue;
            }

            nodesByOperation[entry.Operation] = entry.Node;
        }

        return new DataFlowOperationIndex(nodesByOperation);
    }

    private static IEnumerable<IOperation> EnumerateOperations(NLCPGBuildContext context)
    {
        return context.OperationInventory.Select(entry => entry.Operation);
    }

    private void AddDeclaredSymbolEdges(SyntaxNode syntax, NLCPGNode syntaxNode, NLCPGGraph graph, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(syntax);
        AddDeclaredSymbolEdges(syntaxNode, symbol, graph);
    }

    private static bool CanDeclareSymbol(SyntaxNode syntax)
    {
        return syntax is BaseNamespaceDeclarationSyntax or
          BaseTypeDeclarationSyntax or
          DelegateDeclarationSyntax or
          BaseMethodDeclarationSyntax or
          LocalFunctionStatementSyntax or
          AccessorDeclarationSyntax or
          PropertyDeclarationSyntax or
          IndexerDeclarationSyntax or
          EventDeclarationSyntax or
          EnumMemberDeclarationSyntax or
          VariableDeclaratorSyntax or
          ParameterSyntax or
          TypeParameterSyntax or
          SingleVariableDesignationSyntax or
          UsingDirectiveSyntax or
          ExternAliasDirectiveSyntax or
          LabeledStatementSyntax or
          ForEachStatementSyntax or
          ForEachVariableStatementSyntax or
          CatchDeclarationSyntax or
          FromClauseSyntax or
          JoinClauseSyntax or
          LetClauseSyntax or
          QueryContinuationSyntax;
    }

    private void AddDeclaredSymbolEdges(NLCPGNode syntaxNode, ISymbol? symbol, NLCPGGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, NLCPGEdgeKind.DeclaresSymbol);

        if (symbol is INamedTypeSymbol declaredTypeSymbol)
        {
            // CallGraph worker 会遍历该列表，而本方法由串行的 syntax 路径调用；
            // 与 CallGraph 不重叠，但统一持门以保证"写入对读者可见且不中途变形"。
            lock (_cacheGate)
            {
                _declaredTypes.Add(declaredTypeSymbol);
            }

            var typeDeclNode = GetOrCreateTypeDeclNode(declaredTypeSymbol, graph);
            graph.AddEdge(syntaxNode, typeDeclNode, NLCPGEdgeKind.SyntaxChild);
            graph.AddEdge(typeDeclNode, symbolNode, NLCPGEdgeKind.DeclaresSymbol);
            graph.AddEdge(typeDeclNode, symbolNode, NLCPGEdgeKind.RefersToType);
        }

        if (symbol.ContainingSymbol is not null && symbol.ContainingSymbol.Kind != SymbolKind.NetModule)
        {
            var containerNode = GetOrCreateSymbolNode(symbol.ContainingSymbol, graph);
            graph.AddEdge(containerNode, symbolNode, NLCPGEdgeKind.ContainsSymbol);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            foreach (var baseType in namedType.Interfaces.Cast<ITypeSymbol>().Append(namedType.BaseType).Where(x => x is not null))
            {
                var baseTypeNode = GetOrCreateSymbolNode(baseType!, graph);
                graph.AddEdge(symbolNode, baseTypeNode, NLCPGEdgeKind.BaseType);
                var typeDeclNode = GetOrCreateTypeDeclNode(namedType, graph);
                graph.AddEdge(typeDeclNode, baseTypeNode, NLCPGEdgeKind.InheritsFrom);
            }
        }

        if (symbol is IMethodSymbol declaredMethodSymbol && declaredMethodSymbol.ReturnType is not null)
        {
            var returnTypeNode = GetOrCreateSymbolNode(declaredMethodSymbol.ReturnType, graph);
            graph.AddEdge(symbolNode, returnTypeNode, NLCPGEdgeKind.ReturnsType);
        }

        AddTypeEdges(symbolNode, SymbolTypeOf(symbol), graph);
    }

    private void AddReferencedSymbolEdges(SyntaxNode syntax, NLCPGNode syntaxNode, NLCPGGraph graph, SemanticModel semanticModel)
    {
        if (!CanReferenceSymbol(syntax))
        {
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(syntax).Symbol;
        AddReferencedSymbolEdges(syntax, syntaxNode, symbol, graph);
    }

    private void AddReferencedSymbolEdges(SyntaxNode syntax, NLCPGNode syntaxNode, ISymbol? symbol, NLCPGGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, NLCPGEdgeKind.ReferencesSymbol);

        var referenceNode = graph.AddNode(new NLCPGNodeDraft(
          Kind: NLCPGNodeKind.Reference,
          Name: graph.ResolveName(syntaxNode),
          FullName: graph.ResolveFullName(symbolNode),
          TypeFullName: graph.ResolveTypeFullName(symbolNode),
          FilePath: graph.ResolveFilePath(syntaxNode),
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd));
        graph.AddEdge(syntaxNode, referenceNode, NLCPGEdgeKind.SyntaxChild);
        graph.AddEdge(referenceNode, symbolNode, NLCPGEdgeKind.Ref);
        AddEvalTypeEdge(referenceNode, SymbolTypeOf(symbol), graph);
    }

    private void AddTypeEdges(NLCPGNode sourceNode, ITypeSymbol? typeSymbol, NLCPGGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, NLCPGEdgeKind.HasType);
    }

    private void AddEvalTypeEdge(NLCPGNode sourceNode, ITypeSymbol? typeSymbol, NLCPGGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, NLCPGEdgeKind.EvalType);
    }

    private void AddTypeReferenceEdges(SyntaxNode syntax, NLCPGNode syntaxNode, NLCPGGraph graph, SemanticModel semanticModel, ITypeSymbol? resolvedTypeSymbol = null)
    {
        if (syntax is not TypeSyntax and not ObjectCreationExpressionSyntax and not BaseTypeSyntax)
        {
            return;
        }

        var typeSymbol = resolvedTypeSymbol ?? syntax switch
        {
            TypeSyntax typeSyntax => semanticModel.GetTypeInfo(typeSyntax).Type,
            ObjectCreationExpressionSyntax creation => semanticModel.GetTypeInfo(creation).Type,
            BaseTypeSyntax baseType => semanticModel.GetTypeInfo(baseType.Type).Type,
            _ => null,
        };
        if (typeSymbol is null)
        {
            return;
        }

        var typeRefNode = graph.AddNode(new NLCPGNodeDraft(
          Kind: NLCPGNodeKind.TypeRef,
          Name: typeSymbol.Name,
          FullName: ComposeTypeFullName(typeSymbol),
          TypeFullName: ComposeTypeFullName(typeSymbol),
          FilePath: graph.ResolveFilePath(syntaxNode),
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd));
        graph.AddEdge(syntaxNode, typeRefNode, NLCPGEdgeKind.SyntaxChild);
        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(typeRefNode, typeNode, NLCPGEdgeKind.RefersToType);
    }

    private NLCPGNode GetOrCreateSymbolNode(ISymbol symbol, NLCPGGraph graph)
    {
        var symbolKey = SymbolId(symbol);
        lock (_cacheGate)
        {
            if (_symbolNodes.TryGetValue(graph, symbolKey, out var existing))
            {
                return existing;
            }

            var symbolNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: MapSymbolKind(symbol),
              Name: symbol.Name,
              FullName: ComposeFullName(symbol),
              Signature: ComposeSignature(symbol),
              DispatchKind: symbol is IMethodSymbol methodDispatchSymbol ? ComposeMethodDispatchKind(methodDispatchSymbol) : null,
              TypeFullName: ComposeTypeFullName(SymbolTypeOf(symbol)),
              FilePath: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
              SpanEnd: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
            _symbolKeysByNode[symbolNode] = symbolKey;
            _symbolNodes.Set(graph, symbolKey, symbolNode);
            if (symbol is IMethodSymbol registeredMethodSymbol)
            {
                RegisterMethodSymbol(registeredMethodSymbol);
            }

            return symbolNode;
        }
    }

    private NLCPGNode GetOrCreateTypeDeclNode(INamedTypeSymbol symbol, NLCPGGraph graph)
    {
        var key = $"typedecl:{ComposeFullName(symbol)}";
        lock (_cacheGate)
        {
            if (_typeDeclNodes.TryGetValue(graph, key, out var existing))
            {
                return existing;
            }

            var typeDeclNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: NLCPGNodeKind.TypeDecl,
              Name: symbol.Name,
              FullName: ComposeFullName(symbol),
              Signature: ComposeTypeParameterSignature(symbol),
              TypeFullName: ComposeTypeFullName(symbol),
              FilePath: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
              SpanEnd: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
            _typeDeclNodes.Set(graph, key, typeDeclNode);
            return typeDeclNode;
        }
    }


    private static NLCPGEdgeKind SelectOperationEdge(IOperation parent, IOperation child)
    {
        return parent switch
        {
            IInvocationOperation when child is IArgumentOperation => NLCPGEdgeKind.OpArgument,
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, child) => NLCPGEdgeKind.OpInstance,
            IFieldReferenceOperation fieldReference when ReferenceEquals(fieldReference.Instance, child) => NLCPGEdgeKind.OpInstance,
            IPropertyReferenceOperation when child is IArgumentOperation => NLCPGEdgeKind.OpArgument,
            IPropertyReferenceOperation propertyReference when ReferenceEquals(propertyReference.Instance, child) => NLCPGEdgeKind.OpInstance,
            IReturnOperation when ReferenceEquals(parent.ChildOperations.FirstOrDefault(), child) => NLCPGEdgeKind.OpTarget,
            IConditionalOperation conditional when ReferenceEquals(conditional.Condition, child) => NLCPGEdgeKind.OpCondition,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenTrue, child) => NLCPGEdgeKind.OpWhenTrue,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenFalse, child) => NLCPGEdgeKind.OpWhenFalse,
            ILoopOperation loop when ReferenceEquals(loop.Body, child) => NLCPGEdgeKind.OpBody,
            _ => NLCPGEdgeKind.OpChild,
        };
    }

    private static NLCPGNodeKind MapSymbolKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Namespace => NLCPGNodeKind.SymbolNamespace,
            SymbolKind.NamedType => NLCPGNodeKind.SymbolType,
            SymbolKind.Method => NLCPGNodeKind.SymbolMethod,
            SymbolKind.Property => NLCPGNodeKind.SymbolProperty,
            SymbolKind.Field => NLCPGNodeKind.SymbolField,
            SymbolKind.Local => NLCPGNodeKind.SymbolLocal,
            SymbolKind.Parameter => NLCPGNodeKind.SymbolParameter,
            _ => NLCPGNodeKind.SymbolUnknown,
        };
    }

    private static NLCPGNodeKind MapOperationKind(IOperation operation)
    {
        return operation switch
        {
            IBlockOperation => NLCPGNodeKind.OpBlock,
            IInvocationOperation => NLCPGNodeKind.OpInvocation,
            IArgumentOperation => NLCPGNodeKind.OpArgument,
            IBinaryOperation => NLCPGNodeKind.OpBinary,
            IAssignmentOperation => NLCPGNodeKind.OpAssignment,
            ILocalReferenceOperation => NLCPGNodeKind.OpLocalReference,
            IParameterReferenceOperation => NLCPGNodeKind.OpParameterReference,
            IFieldReferenceOperation => NLCPGNodeKind.OpFieldReference,
            IPropertyReferenceOperation => NLCPGNodeKind.OpPropertyReference,
            ILiteralOperation => NLCPGNodeKind.OpLiteral,
            IReturnOperation => NLCPGNodeKind.OpReturn,
            IBranchOperation branch when branch.BranchKind == BranchKind.Break => NLCPGNodeKind.OpBreak,
            IBranchOperation branch when branch.BranchKind == BranchKind.Continue => NLCPGNodeKind.OpContinue,
            ISwitchOperation => NLCPGNodeKind.OpSwitch,
            ITryOperation => NLCPGNodeKind.OpTry,
            ICatchClauseOperation => NLCPGNodeKind.OpCatch,
            IConditionalOperation => NLCPGNodeKind.OpConditional,
            ILoopOperation => NLCPGNodeKind.OpLoop,
            _ => NLCPGNodeKind.Operation,
        };
    }

    private static ISymbol? ResolveOperationSymbol(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null,
        };
    }

    private static ITypeSymbol? SymbolTypeOf(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            ITypeSymbol type => type,
            _ => null,
        };
    }

    private static bool CanReferenceSymbol(SyntaxNode syntax)
    {
        return syntax is IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax;
    }

    private static string SymbolId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
          ?? $"symbol:{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}:{symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? -1}";
    }

    private static string ComposeFullName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string ComposeTypeFullName(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          .Replace("global::", string.Empty, StringComparison.Ordinal)
          ?? string.Empty;
    }

    private static string ResolveOperationName(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.Name,
            ILocalReferenceOperation localReference => localReference.Local.Name,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter.Name,
            IFieldReferenceOperation fieldReference => fieldReference.Field.Name,
            IPropertyReferenceOperation propertyReference => propertyReference.Property.Name,
            _ => operation.Kind.ToString(),
        };
    }

    private static string? ResolveOperationFullName(IOperation operation)
    {
        return ResolveOperationSymbol(operation) is { } symbol ? ComposeFullName(symbol) : null;
    }

    private static string? ResolveOperationSignature(IOperation operation)
    {
        return ResolveOperationSymbol(operation) is { } symbol ? ComposeSignature(symbol) : null;
    }


    private static IMethodSymbol CanonicalMethodSymbol(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is null)
        {
            return methodSymbol;
        }

        return methodSymbol.ReducedFrom;
    }

    private static string ComposeMethodFullName(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var containingType = methodSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(methodSymbol.ContainingType) + ".";
        return $"{containingType}{ComposeMethodName(methodSymbol)}:{ComposeMethodSignature(methodSymbol)}";
    }

    private static string ComposeInvocationMethodFullName(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(methodSymbol.ContainingType) + ".";
        return $"{containingType}{ComposeInvocationMethodName(methodSymbol)}:{ComposeInvocationSignature(methodSymbol)}";
    }

    private static string ComposeMethodSignature(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var parameterTypes = string.Join(",", methodSymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        var returnType = ComposeTypeFullName(methodSymbol.ReturnType);
        var genericSuffix = ComposeMethodInstantiationKey(methodSymbol);
        return $"{returnType}{genericSuffix}({parameterTypes})";
    }

    private static string ComposeInvocationSignature(IMethodSymbol methodSymbol)
    {
        var parameterTypes = string.Join(",", methodSymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        var returnType = ComposeTypeFullName(methodSymbol.ReturnType);
        var genericSuffix = ComposeMethodInstantiationKey(methodSymbol);
        return $"{returnType}{genericSuffix}({parameterTypes})";
    }

    private static string ComposeMethodLookupKey(IMethodSymbol methodSymbol)
    {
        return $"{ComposeMethodName(methodSymbol)}:{ComposeMethodSignature(methodSymbol)}";
    }

    private static string ComposeMethodName(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return ".ctor";
        }

        if (methodSymbol.MethodKind == MethodKind.StaticConstructor)
        {
            return ".cctor";
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation &&
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            var implementedMethod = methodSymbol.ExplicitInterfaceImplementations[0];
            return $"{ComposeTypeFullName(implementedMethod.ContainingType)}.{implementedMethod.Name}";
        }

        return methodSymbol.Name;
    }

    private static string ComposeInvocationMethodName(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return ".ctor";
        }

        if (methodSymbol.MethodKind == MethodKind.StaticConstructor)
        {
            return ".cctor";
        }

        return methodSymbol.Name;
    }

    private static string ComposeMethodInstantiationKey(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        if (methodSymbol.TypeArguments.Length == 0 && methodSymbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var typeParameters = methodSymbol.TypeArguments.Length > 0
          ? methodSymbol.TypeArguments.Select(ComposeGenericTypeIdentity)
          : methodSymbol.TypeParameters.Select(parameter => $"{parameter.Ordinal}:{parameter.Name}");
        return $"<{string.Join(",", typeParameters)}>";
    }

    private static string ComposeGenericTypeIdentity(ITypeSymbol typeSymbol)
    {
        return typeSymbol switch
        {
            ITypeParameterSymbol typeParameter => $"{typeParameter.Ordinal}:{typeParameter.Name}",
            _ => ComposeTypeFullName(typeSymbol),
        };
    }

    private static string ComposeSignature(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol methodSymbol => ComposeMethodSignature(methodSymbol),
            INamedTypeSymbol typeSymbol => ComposeTypeParameterSignature(typeSymbol),
            IPropertySymbol propertySymbol => ComposePropertySignature(propertySymbol),
            IFieldSymbol fieldSymbol => ComposeTypeFullName(fieldSymbol.Type),
            ILocalSymbol localSymbol => ComposeTypeFullName(localSymbol.Type),
            IParameterSymbol parameterSymbol => ComposeTypeFullName(parameterSymbol.Type),
            _ => string.Empty,
        };
    }

    private static NLCPGDispatchKind ComposeCallDispatchKind(IMethodSymbol methodSymbol, bool hasInstance = true)
    {
        var flags = (IsInternalMethod(methodSymbol)
          ? NLCPGDispatchFlags.Internal
          : NLCPGDispatchFlags.External) |
          NLCPGDispatchFlags.Dispatch;
        if (methodSymbol.IsExtensionMethod)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags |
              NLCPGDispatchFlags.Extension |
              (hasInstance ? NLCPGDispatchFlags.Instance : NLCPGDispatchFlags.Static));
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.InterfaceImplementation);
        }

        if (!hasInstance || methodSymbol.IsStatic)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Static);
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Interface | NLCPGDispatchFlags.Dispatch);
        }

        if (methodSymbol.IsOverride)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Override | NLCPGDispatchFlags.Dispatch);
        }

        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Virtual | NLCPGDispatchFlags.Dispatch);
        }

        return new NLCPGDispatchKind(
          NLCPGDispatchCategory.Method,
          flags | NLCPGDispatchFlags.Static);
    }

    private static NLCPGDispatchKind ComposePropertyAccessorDispatchKind(IMethodSymbol methodSymbol, bool hasInstance)
    {
        var baseDispatch = ComposeCallDispatchKind(methodSymbol, hasInstance);
        var isIndexer = methodSymbol.AssociatedSymbol is IPropertySymbol { Parameters.Length: > 0 };
        var accessorFlags = isIndexer ? NLCPGDispatchFlags.Indexer : NLCPGDispatchFlags.None;
        if (methodSymbol.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            return baseDispatch with
            {
              Flags = baseDispatch.Flags | accessorFlags | NLCPGDispatchFlags.PropertyGet
            };
        }

        if (methodSymbol.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return baseDispatch with
            {
              Flags = baseDispatch.Flags | accessorFlags | NLCPGDispatchFlags.PropertySet
            };
        }

        return methodSymbol.AssociatedSymbol is IPropertySymbol
          ? baseDispatch with
          {
            Flags = baseDispatch.Flags | accessorFlags | NLCPGDispatchFlags.PropertyAccessor
          }
          : baseDispatch;
    }

    private static NLCPGDispatchKind ComposeResolvedDispatchKind(IMethodSymbol resolvedMethod, IMethodSymbol requestedMethod, ITypeSymbol? receiverType, NLCPGDispatchKind baseDispatchKind)
    {
        if (!IsInternalMethod(resolvedMethod))
        {
            return baseDispatchKind with
            {
              Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.ExternalFallback
            };
        }

        if (string.Equals(ComposeMethodFullName(resolvedMethod), ComposeMethodFullName(requestedMethod), StringComparison.Ordinal))
        {
            return baseDispatchKind with
            {
              Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.Exact
            };
        }

        if (receiverType is INamedTypeSymbol namedReceiverType && resolvedMethod.ContainingType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(resolvedMethod.ContainingType, namedReceiverType))
            {
                return baseDispatchKind with
                {
                  Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.ReceiverExact
                };
            }

            if (InheritsFrom(namedReceiverType, resolvedMethod.ContainingType))
            {
                return baseDispatchKind with
                {
                  Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.Hierarchy
                };
            }
        }

        return baseDispatchKind with
        {
          Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.Fallback
        };
    }

    private static NLCPGDispatchKind ComposeMethodDispatchKind(IMethodSymbol methodSymbol)
    {
        var flags = (IsInternalMethod(methodSymbol)
          ? NLCPGDispatchFlags.Internal
          : NLCPGDispatchFlags.External) |
          NLCPGDispatchFlags.Definition;
        if (methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is not null)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Extension);
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.InterfaceImplementation);
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Interface);
        }

        if (methodSymbol.IsOverride)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Override);
        }

        if (methodSymbol.IsAbstract)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Abstract);
        }

        if (methodSymbol.IsVirtual)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Virtual);
        }

        return new NLCPGDispatchKind(
          NLCPGDispatchCategory.Method,
          flags |
          (methodSymbol.IsStatic ? NLCPGDispatchFlags.Static : NLCPGDispatchFlags.Instance));
    }

    private static bool IsInternalMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Locations.Any(location => location.IsInSource);
    }

    private static string ComposeTypeParameterSignature(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeArguments.Length == 0 && typeSymbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var typeParameters = typeSymbol.TypeArguments.Length > 0
          ? typeSymbol.TypeArguments.Select(ComposeTypeFullName)
          : typeSymbol.TypeParameters.Select(parameter => parameter.Name);
        return $"<{string.Join(",", typeParameters)}>";
    }

    private static string ComposePropertySignature(IPropertySymbol propertySymbol)
    {
        if (propertySymbol.Parameters.Length == 0)
        {
            return ComposeTypeFullName(propertySymbol.Type);
        }

        var parameterTypes = string.Join(",", propertySymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        return $"{ComposeTypeFullName(propertySymbol.Type)}[{parameterTypes}]";
    }


    private static bool InheritsFrom(INamedTypeSymbol candidateType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(candidateType, targetType))
        {
            return true;
        }

        foreach (var baseType in candidateType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, targetType))
            {
                return true;
            }
        }

        for (var current = candidateType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetType))
            {
                return true;
            }
        }

        return false;
    }

    private static string NameOfMethod(BaseMethodDeclarationSyntax declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => declaration.Kind().ToString(),
        };
    }

    private static string Shorten(string text, int maxLength = 120)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    internal static IReadOnlyList<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return Array.Empty<MetadataReference>();
        }

        return trustedPlatformAssemblies
          .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
          .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
          .Select(path => MetadataReference.CreateFromFile(path))
          .ToList();
    }
}