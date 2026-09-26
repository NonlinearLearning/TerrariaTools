using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P 的**事实基础**（轮次 16）：把 `NLCPGBuilder` 里**隐式的阶段依赖**从「靠固定调用顺序」
/// 变成**可测断言**。
/// <para>
/// <b>为什么这是 G0-P 而不是测试洁癖：</b>`NLCPGBuilder.cs:411-417` 用
/// <c>RunOptionalPass(buildPlan.Requires*, ...)</c> 驱动 7 个后置 pass。但
/// <c>Requires*</c>（`:583-589`）**只表达「该能力是否被请求」**，来自 capability 位，
/// **完全不表达阶段之间的依赖**（例如「DataFlow 必须在 CallGraph 之后」）。
/// 真实依赖是**隐式**的，仅由那段代码的**书写顺序**保证。
/// </para>
/// <para>
/// <b>为什么 S3 会踩到：</b>S3-1 要求把 8 个 pass 拆成「计划/计算/归并」并由窗口协调器
/// 重新组织提交。**任何重排都会打破只靠顺序维系的隐式依赖**，且破坏方式是
/// <b>静默地少算边</b>（不是崩溃）。
/// </para>
/// <para>
/// ⚠️ <b>本测试的断言在轮次 16 被自己的变异实验修正了两次，必须记住最终形态：</b>
/// <list type="number">
/// <item>最初断言「依赖一被**运行期强制**」（依据 `DataFlowPass.cs:1936` 会抛
/// <c>InvalidOperationException</c>）。<b>实测证伪</b>：跳过 `CallGraphPass` 后仍然通过
/// ⇒ 该异常在正常构建路径上**不可达**。</item>
/// <item>改为断言 <c>DataFlow</c> 边非空。<b>再次证伪</b>：跳过 `CallGraphPass` 后**仍通过**。
/// 原因经测量确定：<c>DataFlow</c> capability 产出的是**方法内**数据流（15 条），
/// 它**不经过** CallGraph ⇒ 夹具**结构性地区分不了**。</item>
/// <item>最终改用 <c>InterproceduralDataFlow</c> 并断言**跨过程桥接边**非空——
/// 这正是同伴 `CpgInterproceduralEdgeOrderTests` 在同一变异下失败 3 条所走的路径
/// （`Assert.NotEmpty() Failure: Collection was empty`）。</item>
/// </list>
/// 教训与本轮附录 M 同源：**夹具能通过 ≠ 夹具能判别**；只有变异能区分。
/// </para>
/// </summary>
public sealed class BuilderStageDependencyContractTests
{
    /// <summary>
    /// 依赖一的**真实可观察后果**：跨过程桥接边（`ArgumentToParameter` / `ReturnToCallSite` 等）
    /// 的存在**依赖** `CallGraphPass` 已经跑过。
    /// <para>
    /// <b>为什么用跨过程而不是方法内 `DataFlow`：</b>实测表明跳过 `CallGraphPass` 后
    /// 方法内 `DataFlow` 边**照常产出**（夹具区分不了），而**跨过程桥接边静默变空**。
    /// 前者是"看起来在测依赖、实际测不到"的空转形态。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenInterproceduralRequested_EmitsCrossMethodBridgeEdges()
    {
        var graph = Build(new[] { NLCPGCapability.InterproceduralDataFlow });

        var bridgeEdges = GetBridgeEdges(graph);

        Assert.NotEmpty(bridgeEdges);
    }

    /// <summary>
    /// 依赖二：`InterproceduralDataFlow` 要求 `CallGraph` 与 `DataFlow` 的 reducer
    /// **都已完成**——`NLCPGBuilder.cs:1041-1044` 在进入该阶段时置
    /// <c>_interproceduralBarrierCompleted = true</c>，并在注释中明文声明该前提。
    /// <para>
    /// 与依赖一不同，这一条**连不可达的守卫都没有**，是**纯注释约定**
    /// ⇒ S3 重排时**没有任何东西会拦住你**。故本测试用**可观察产物**固定它。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenInterproceduralRequested_BridgesCoverMultipleKinds()
    {
        var graph = Build(new[] { NLCPGCapability.InterproceduralDataFlow });

        // 桥接种类由边的 `StructuredLabel.StableKey` 承载（读法与同伴 oracle 一致）。
        var bridgeKinds = GetBridgeEdges(graph)
          .Select(edge => edge.StructuredLabel?.StableKey ?? string.Empty)
          .Select(label => label.StartsWith("interprocedural-bridge:", StringComparison.Ordinal)
            ? label["interprocedural-bridge:".Length..]
            : label)
          .Where(label => label.Length > 0)
          .Distinct(StringComparer.Ordinal)
          .ToList();

        // 至少两种桥接种类：单向缺失说明跨过程阶段只跑了一半。
        Assert.True(
          bridgeKinds.Count >= 2,
          $"跨过程桥接种类不足（实测 {bridgeKinds.Count} 种：{string.Join(",", bridgeKinds)}）；"
          + "单向产出说明该阶段只完成了一部分。");
    }

    /// <summary>
    /// 依赖三（**结构性**，来源确证）：`Requires*` 标志来自 capability 位，
    /// **不是**阶段依赖描述。
    /// <para>
    /// 判据：请求 `InterproceduralDataFlow` 时，capability 集合里**没有** `CallTargets`，
    /// 但构建仍然成功且产出桥接边 ⇒ 该依赖由**调用顺序**（CallGraph 在前）提供，
    /// **不是** capability 蕴含的。若有人把 `Requires*` 误读成「依赖已声明」并据此重排
    /// 阶段，本测试的前提就会失效。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenOnlyInterproceduralRequested_StillProducesBridgesSoOrderingSuppliesTheDependency()
    {
        var graph = Build(new[] { NLCPGCapability.InterproceduralDataFlow });

        Assert.NotEmpty(graph.Nodes);
        Assert.NotEmpty(GetBridgeEdges(graph));

        WriteEvidence(graph);
    }

    /// <summary>跨过程桥接边：只保留真正依赖 CallGraph 的边种（与同伴 oracle 一致）。</summary>
    private static IReadOnlyList<NLCPGEdge> GetBridgeEdges(NLCPGGraph graph)
    {
        return graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
          .ToList();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 轮次 17：把依赖表从 2 条扩到 4 条，并固定「依赖总数」这一结构性事实
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 依赖三：`ControlDependence` **依赖** `Dominance` 填充的 <c>_dominanceOverlays</c>。
    /// <para>
    /// 源码：`ControlDependencePass.cs:35` —— <c>if (_dominanceOverlays.Count == 0) return;</c>。
    /// 该字段由 `DominancePass` 写入（`:336` / `:376`），是 **`NLCPGBuilder` 的实例字段**，
    /// 即 pass 之间的**隐式状态通道**。⇒ 若 S3 把两者拆成独立阶段而不同步该状态，
    /// `ControlDependence` 会**静默地什么都不做**（提前 return），既不抛异常也无日志。
    /// </para>
    /// <para>
    /// ⚠️ 夹具要求：控制依赖边**只在存在分支**时才产生。最初我复用了类里那份
    /// 无分支的 `CallSource`，结果该用例失败（`Collection was empty`）——
    /// 又一次「夹具选错观测面」。此处改用仓库既有夹具
    /// <c>CpgBuilderSources.ControlDependenceOverlay</c>（含 if/else），与同伴
    /// `NLCPGPartitionedBuilderTests` 同源。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlDependenceRequested_EmitsControlDependenceEdges()
    {
        var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.ControlDependence },
        }).BuildFromSource(
          CpgBuilderSources.ControlDependenceOverlay,
          "stage-dependency-control-dependence.cs");

        var controlDependenceEdges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.ControlDependence)
          .ToList();

        Assert.NotEmpty(controlDependenceEdges);
    }

    /// <summary>
    /// 依赖四：`DataFlow` 的 **CFG 邻接规划**读取 `ControlFlow` 写入的
    /// <c>_cfgPredecessorsByNode</c>/<c>_cfgSuccessorsByNode</c>。
    /// <para>
    /// 源码：`DataFlowPass.cs:1227-1229` 调 <c>GetCachedCfgPredecessors/Successors</c>；
    /// 该缓存由 `AddControlFlowEdge`→`AddCfgNeighbor`（`NLCPGBuilder.cs:2062-2063`）填充，
    /// 而 `AddControlFlowEdge` 只在 `ControlFlowPass` 中调用。
    /// ⇒ **又一条无守卫的隐式通道**：顺序反转会让邻接规划读到空缓存。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowRequested_CfgAdjacencyCacheWasPopulatedByControlFlow()
    {
        var graph = Build(new[] { NLCPGCapability.DataFlow });

        // ControlFlow 的产物：CFG 边。它是依赖四所读缓存的来源。
        var cfgEdges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.CfgNext)
          .ToList();

        Assert.NotEmpty(cfgEdges);
    }

    /// <summary>
    /// 依赖五：`DataFlow` **消费** `MemberAccess` 的产物——
    /// 跳过 `MemberAccessPass` 会让数据流边集变化。
    /// <para>
    /// <b>证据来源（轮次 18 反向变异）：</b>逐个把后置 pass 的门控置 false 后跑
    /// <c>FullyQualifiedName~Cpg</c>（基线 546/546 **全绿**）：
    /// 跳过 `MemberAccess` → **4 条失败**，其中
    /// <c>NLCPGPartitionedBuilderTests.BuildFromSource_OperationConsumers_PreserveCallMemberAndDataFlowAcrossDegreesOfParallelism</c>
    /// 与 3 条 <c>DataFlowAdjacencyCompactionTests.BuildFromSource_EveryFixture_KeepsFrozenGraphAndPublicationOrder</c>
    /// 都含期望的数据流边数 ⇒ **DataFlow 确实消费 MemberAccess 的产物**。
    /// </para>
    /// <para>
    /// <b>本条依赖此前未被记载</b>——轮次 17 只确证了 4 条，且当时判断
    /// 「`MemberAccess` 未测得对前序 pass 的依赖」，但**方向搞反了**：
    /// `MemberAccess` 不是消费者，而是**被消费者**。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowRequested_MemberAccessArtefactsArePresent()
    {
        var graph = Build(new[] { NLCPGCapability.DataFlow });

        // MemberAccess 的产物：AccessesMember / Ref 边。
        // 它们若缺失，依赖五被破坏（DataFlow 会读到不完整的成员访问信息）。
        var memberAccessEdges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.AccessesMember)
          .ToList();

        Assert.NotEmpty(memberAccessEdges);
    }

    /// <summary>
    /// 依赖六：`DataFlow` 通过 <c>_propertyAccessorCallSiteNodesByKey</c> 查调用点，
    /// 而该字段**由 `CallGraphPass` 写入**（`CallGraphPass.cs:238`）。
    /// <para>
    /// 读点是 <c>DataFlowPass.cs:2103</c>，未命中时**返回 <c>null</c>（静默降级）**，
    /// **不抛异常** ⇒ 又一条零守卫通道，且与依赖一**是 CallGraph→DataFlow 的第二条独立通道**
    /// （依赖一走 `_resolvedCallTargetsByInvocation`）。
    /// </para>
    /// <para>
    /// ⚠️ <b>判据在轮次 18 被变异证伪过一次（第五次同类错误），必须记住修正后的形态：</b>
    /// 最初断言「请求 <c>DataFlow</c> 时 <c>Ref</c> 边非空」。**跳过 `CallGraphPass` 后仍然通过**——
    /// 因为 <c>Ref</c> 边由 `MemberAccessPass` 自己也能产出（`MemberAccessPass.cs:152`），
    /// **不是 CallGraph 的专属产物**。⇒ 与轮次 16/17 同形：夹具选了一个**跨阶段都会产出**的量。
    /// </para>
    /// <para>
    /// 修正后改用**桥接边**（`InterproceduralDataFlow`）——它是 CallGraph 的**专属**产物。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenInterproceduralRequested_BridgesRequireCallGraphCanResolvePropertyAccessors()
    {
        var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
        }).BuildFromSource(PropertySource, "stage-dependency-property.cs");

        // 桥接边只在 CallGraph 解析出调用目标后才存在（依赖一/六的共同前提）。
        // 属性访问路径必须先被 CallGraph 登记到 _propertyAccessorCallSiteNodesByKey，
        // 否则 DataFlowPass.cs:2103 返回 null 而桥接边缺失。
        var bridgeEdges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
          .ToList();

        Assert.NotEmpty(bridgeEdges);
    }

    /// <summary>含属性访问的最小源：触发 `_propertyAccessorCallSiteNodesByKey` 的读写路径。</summary>
    private const string PropertySource = """
        namespace Demo;

        public sealed class Holder
        {
            public int Value { get; set; }
        }

        public sealed class User
        {
            private readonly Holder _holder = new();

            public int RoundTrip(int input)
            {
                _holder.Value = input;
                return _holder.Value + 1;
            }
        }
        """;

    /// <summary>
    /// **结构性事实**：`Requires*` 门控标志与「阶段依赖」是两回事，
    /// 且依赖**不只**存在于被请求的能力之间——它由 `NLCPGBuilder` 的
    /// **实例字段**承载（轮次 17 扫描出 30+ 个跨阶段可变字段）。
    /// <para>
    /// 本测试固定**可观察的必要条件**：只请求 `DataFlow` 时，构建必须同时产出
    /// CFG 边（依赖四的来源）与 DataFlow 边。这证明**依赖的满足来自调用顺序**，
    /// 而不是来自 capability 集合——后者此时并不含 `Cfg`。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenOnlyDataFlowRequested_StillHasCfgEdgesProvingOrderSuppliesThem()
    {
        var graph = Build(new[] { NLCPGCapability.DataFlow });

        var cfgEdges = graph.Edges
          .Count(edge => edge.Kind == NLCPGEdgeKind.CfgNext);

        Assert.True(
          cfgEdges > 0,
          "只请求 DataFlow 时没有 CFG 边。依赖四的来源（ControlFlow 写入的邻接缓存）"
          + "若不存在，说明该依赖的满足方式已变化，需重新评估阶段依赖表。");

        WriteEvidence(graph);
    }

    private static NLCPGGraph Build(NLCPGCapability[] capabilities)
    {
        return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = capabilities,
        }).BuildFromSource(CallSource, "stage-dependency.cs");
    }

    private static void WriteEvidence(NLCPGGraph graph)
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                   !(File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                     Directory.Exists(Path.Combine(directory.FullName, "src"))))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                return;
            }

            var path = Path.Combine(
              directory.FullName,
              "Build",
              "g0m-calibration",
              "builder-stage-dependencies.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var byKind = graph.Edges
              .GroupBy(edge => edge.Kind)
              .OrderBy(group => group.Key)
              .Select(group => $"{group.Key}={group.Count()}");

            File.WriteAllText(
              path,
              $"nodes: {graph.Nodes.Count}{Environment.NewLine}"
              + $"edges: {graph.Edges.Count}{Environment.NewLine}"
              + $"edgesByKind: {string.Join(", ", byKind)}{Environment.NewLine}"
              + "implicitDependencies (from source; FOUR confirmed by round 17):"
              + Environment.NewLine
              + "  1. DataFlow requires CallGraph -- consequence is SILENTLY EMPTY bridge edges,"
              + " NOT an exception."
              + Environment.NewLine
              + "     The guard at DataFlowPass.cs:1934-1937 exists but was measured UNREACHABLE"
              + " on the normal build path."
              + Environment.NewLine
              + "  2. InterproceduralDataFlow requires CallGraph+DataFlow reducers complete"
              + " -- comment-only (NLCPGBuilder.cs:1041-1044), NO guard at all."
              + Environment.NewLine
              + "  3. ControlDependence requires Dominance to have filled _dominanceOverlays"
              + " -- ControlDependencePass.cs:35 silently RETURNS when the list is empty."
              + Environment.NewLine
              + "  4. DataFlow CFG-adjacency planning reads the cache written by ControlFlow"
              + " -- DataFlowPass.cs:1227-1229 reads _cfgPredecessors/SuccessorsByNode,"
              + Environment.NewLine
              + "     written only via AddControlFlowEdge (ControlFlowPass)."
              + Environment.NewLine
              + "  => Dependencies 2/3/4 have NO runtime guard; 1 has one that is unreachable."
              + Environment.NewLine
              + "  => CRITICAL for S3: the channels are 30+ MUTABLE INSTANCE FIELDS on NLCPGBuilder,"
              + " not parameters."
              + Environment.NewLine
              + "     Requires* flags (NLCPGBuilder.cs:583-589) come from capability bits and do NOT"
              + " encode stage dependencies."
              + Environment.NewLine);
        }
        catch (IOException)
        {
            // 证据落盘失败不影响契约判定。
        }
    }

    /// <summary>含跨方法调用的最小源：调用点必须有内部方法目标，跨过程阶段才有产物。</summary>
    private const string CallSource = """
        namespace Demo;

        public sealed class Helper
        {
            public int Compute(int value)
            {
                return value + 1;
            }
        }

        public sealed class Caller
        {
            private readonly Helper _helper = new();

            public int Run(int input)
            {
                var intermediate = _helper.Compute(input);
                return _helper.Compute(intermediate);
            }
        }
        """;
}
