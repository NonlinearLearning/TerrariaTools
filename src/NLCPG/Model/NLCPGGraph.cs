using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NLCPG.Contracts;

namespace NLCPG.Model;

/// 管理NLCPG 的内存节点集和边集。
public sealed class NLCPGGraph
{
    // 锚点 -> 构图期序数。存序数而非节点，value 由 8 B 引用降为 4 B int；
    // 节点本体存于 _nodesByOrdinal，二者一一对应。
    private readonly Dictionary<StableNodeAnchor, int> _mutableNodesByAnchor = new();
    // 构图期局部序数：ordinal -> node，按首次入图顺序追加。
    // 边载荷改存 4 B 序数而非 28 B 锚点；序数与锚点一一对应，故去重语义不变。
    // 与 NodeId 无关——峰值出现在 FreezeQueryIndex 之前，此时 NodeId 尚不存在。
    private readonly List<NLCPGNode> _nodesByOrdinal = new();
    // 冻结后完整节点只存一份（NLCPGGraphIndex 的 canonical 数组）。原先此处的
    // Dictionary<NodeId, NLCPGNode> 会把 NLCPGNode（实测 104 B）按值内联进每个 entry，
    // 是冻结后常驻节点存储的最大单项，现已整体移除：
    // 按 NodeId 读取改走 NLCPGGraphIndex.TryGetNode / GetKnownNode（复用其 NodeId→ordinal 字典），
    // graph.Nodes 改走 NLCPGGraphIndex.InputOrderedNodes（同一数组 + 4 B/节点的输入序排列）。
    private readonly PendingEdgeBuffer _pendingEdges = new();
    private readonly Dictionary<string, string> _sourceByPath = new(StringComparer.Ordinal);
    private readonly StringInterner _stringInterner;
    private readonly StableNodeIdentityFactory _identityFactory;
    private readonly DeterministicNodeIdTable? _preallocatedNodeIds;
    private readonly Action<StableNodeAnchor>? _anchorDiscoveryObserver;
    private NLCPGGraphIndex? _queryIndex;

    // ── G0-P 附录 R.3「分层」：L0 规划层必须【只读】────────────────────────────
    // 设计把 L0 定义为"只读，无 worker，窗口前完成"，L2 是"唯一写图者"。
    // 但此前 L0 的只读性**仅由 NLCPGBuilder.cs 的一段注释声明**，无任何守卫——
    // 正是附录 N.4/P.4 反复踩到的"声明代替机制"形态：把该性质改成可被违反，
    // 而唯一发现途径是"某个测试恰好断言了图内容"。
    //
    // 本字段把它变成 fail-closed：规划相位期间任何构图调用都会抛出并指出违反的层次。
    // ⚠ 刻意用【深度计数】而非 bool：嵌套窗口（未来若有）退化为 bool 会让内层退出
    //   把外层也一并关掉，从而**静默**留下一个不再受保护的外层窗口。
    private int _readOnlyWindowDepth;

    // 只读窗口被**进入**的累计次数——供契约测试判定"窗口真的开过"。
    // 为什么需要：若把 EnterReadOnlyWindow 从规划相位移除，守卫变成**死代码**，
    // 而所有"窗口内构图会抛"的用例仍然全绿（因为没人进窗口）。
    // 那个形态与"没有守卫"在行为上**完全等价**，故必须有一个计数器证明它被执行过。
    // 与 DataFlowPlanAssemblyCount 同源（附录 X.5）：行为层不可观测的差异，只能靠计数层判别。
    private int _readOnlyWindowEntryCount;

    /// <summary>
    /// 进入**只读窗口**（G0-P 附录 R.3 的 L0 规划层）。窗口内任何
    /// <see cref="AddNode(NLCPGNodeDraft, NodeId?, StableNodeAnchor?)"/> /
    /// <see cref="AddEdge"/> 等构图调用都会**抛出**。
    /// <para>
    /// 必须与 <see cref="ExitReadOnlyWindow"/> 成对，且用 <c>finally</c> 闭合：
    /// 规划中途抛异常而未退出，会让**后续执行相位**的合法构图全部误抛。
    /// </para>
    /// </summary>
    internal void EnterReadOnlyWindow()
    {
        _readOnlyWindowDepth++;
        _readOnlyWindowEntryCount++;
    }

    /// <summary>退出只读窗口。与 <see cref="EnterReadOnlyWindow"/> 必须严格配对。</summary>
    internal void ExitReadOnlyWindow()
    {
        if (_readOnlyWindowDepth > 0)
        {
            _readOnlyWindowDepth--;
        }
    }

    /// <summary>
    /// 当前**是否处于**只读窗口内。
    /// <para>
    /// ⚠ 它只说明"此刻"，**不能**说明"曾经开过"——后者由
    /// <see cref="ReadOnlyWindowEntryCount"/> 回答，两者用途不同。
    /// </para>
    /// </summary>
    internal bool IsInReadOnlyWindow => _readOnlyWindowDepth > 0;

    /// <summary>
    /// 只读窗口被进入过的**累计次数**——用于排除"守卫是死代码"。
    /// <para>
    /// <see cref="IsInReadOnlyWindow"/> 只能证明"此刻在窗口内"，
    /// 无法证明规划相位**曾经**进入过它；而"从未进入"与"没有守卫"行为等价。
    /// </para>
    /// </summary>
    internal int ReadOnlyWindowEntryCount => _readOnlyWindowEntryCount;

    // ── G0-P 附录 R.3「分层」：L1 计算层必须【只算 fragment，不写共享图】──────
    // 设计把 L1 定义为"worker 内只算 fragment，不写共享图"，L2 是"唯一写图者"。
    // 但此前该性质**仅由注释与书写习惯维系**——例如 PartitionedSyntaxPass 的
    // "不创建图节点，避免 worker 线程污染共享状态"只是一句注释，零机制（附录 AB）。
    //
    // ⚠ 作用域必须【按图实例 + 按线程】，两者都是实测约束，不是保守起见：
    //   1. 按图实例——worker 内会新建私有 localGraph 并写它（ControlFlowPass:94、
    //      ControlDependencePass:125、DominancePass:451 各一处）；若做成"worker 期间
    //      禁写任何图"，会当场打断这三个阶段的构建。
    //   2. 按线程——reducer 与 worker **并发**运行（CpgWorkBatchExecutor 先起
    //      reducerTask 再起 workers），而 reducer 正是 L2、**必须**能写共享图；
    //      若用图级/进程级计数，DataFlow 的合法归并写入会被误报。
    //   故用 ThreadLocal 把"窗口开着"这一事实限制在该 worker 线程内。
    private readonly ThreadLocal<int> _workerComputeWindowDepth = new ThreadLocal<int>();

    // 窗口被进入的累计次数——用于排除"守卫是死代码"（与 _readOnlyWindowEntryCount 同源）：
    // 若 executor 从不进入窗口，守卫永不执行，而所有"窗口内写图会抛"的用例仍然全绿，
    // 那个形态与"没有守卫"在观测上不可区分。
    // ⚠ 由多个 worker 线程并发递增，故必须用 Interlocked（L0 那个是单线程的，可以直接 ++）。
    private int _workerComputeWindowEntryCount;

    // AddNode 命中锚点、但 MergeNode 未改动任何字段 ⇒ 跳过写回的累计次数。
    //
    // 与 _workerComputeWindowEntryCount 同一理由：短路分支本身只能证明"跳过了"，
    // 无法证明它**曾经被走到**。而"从未走到"与"没有这个分支"在观测上不可区分——
    // 若真实语料里重放总在改动节点，短路就是死代码，而全部断言仍然全绿。
    // 发布段在串行区，但其他 pass 也在 worker 内 AddNode，故仍用 Interlocked。
    private int _redundantMergeSkipCount;

    internal int RedundantMergeSkipCount => Volatile.Read(ref _redundantMergeSkipCount);

    /// <summary>
    /// 进入 **worker 计算窗口**（G0-P 附录 R.3 的 L1 计算层）。窗口内对<b>本图</b>的写操作会抛出。
    /// <para>
    /// 由 <see cref="Concurrency.CpgWorkBatchExecutor"/> 在每个 worker 调用 <c>processBatch</c>
    /// 前后成对开启/关闭，故**全部** pass 自动受保护，无需逐阶段接线。
    /// 必须与 <see cref="ExitWorkerComputeWindow"/> 用 <c>finally</c> 配对：worker 抛异常
    /// 而未退出，会让同一线程后续的合法构图被误报（与 L0 窗口同一注意事项）。
    /// </para>
    /// </summary>
    internal void EnterWorkerComputeWindow()
    {
        _workerComputeWindowDepth.Value += 1;
        Interlocked.Increment(ref _workerComputeWindowEntryCount);
    }

    /// <summary>退出 worker 计算窗口。与 <see cref="EnterWorkerComputeWindow"/> 必须严格配对。</summary>
    internal void ExitWorkerComputeWindow()
    {
        if (_workerComputeWindowDepth.Value > 0)
        {
            _workerComputeWindowDepth.Value -= 1;
        }
    }

    /// <summary>当前线程**是否处于**本图的 worker 计算窗口内。</summary>
    internal bool IsInWorkerComputeWindow => _workerComputeWindowDepth.Value > 0;

    /// <summary>
    /// worker 计算窗口被进入过的**累计次数**——用于排除"守卫是死代码"。
    /// <para>
    /// <see cref="IsInWorkerComputeWindow"/> 只说明"此刻"，无法证明构建过程**曾经**
    /// 进入过它；而"从未进入"与"没有守卫"在行为上等价（附录 X.5/Y.4 同型教训）。
    /// </para>
    /// </summary>
    internal int WorkerComputeWindowEntryCount => Volatile.Read(ref _workerComputeWindowEntryCount);

    // 初始化可变 CPG 图，并可选接入预分配 NodeId 与锚点发现回调。
    public NLCPGGraph(DeterministicNodeIdTable? preallocatedNodeIds = null, StableNodeIdentityFactory? identityFactory = null, Action<StableNodeAnchor>? anchorDiscoveryObserver = null, StringInterner? stringInterner = null)
    {
        _preallocatedNodeIds = preallocatedNodeIds;
        _identityFactory = identityFactory ?? new StableNodeIdentityFactory();
        _anchorDiscoveryObserver = anchorDiscoveryObserver;
        _stringInterner = stringInterner ?? new StringInterner();
    }

    internal static NLCPGGraph CreateAnchorDiscovery(StableNodeIdentityFactory identityFactory, Action<StableNodeAnchor> observeAnchor)
    {
        ArgumentNullException.ThrowIfNull(identityFactory);
        ArgumentNullException.ThrowIfNull(observeAnchor);
        return new NLCPGGraph(identityFactory: identityFactory, anchorDiscoveryObserver: observeAnchor);
    }

    // 冻结后由查询索引的输入序视图提供，与索引的 canonical 数组共用同一份节点载荷。
    public IReadOnlyCollection<NLCPGNode> Nodes =>
      _queryIndex is null ? _nodesByOrdinal : _queryIndex.InputOrderedNodes;

    // 冻结后的 canonical（NodeId 升序）节点序，直接返回索引自身持有的数组实例。
    // 供分片导出这类只读消费者使用：它们需要"按 NodeId 升序"的稳定序来决定分片局部序号，
    // 而该序正是索引的 canonical 序，故无需再复制一份节点载荷。
    internal IReadOnlyList<NLCPGNode> CanonicalNodes => RequireQueryIndex().OrderedNodes;

    // 冻结后由查询索引的投影视图提供（插入序，4 B/边）；冻结前恒为空，与
    // 原先 `_edges` HashSet 在冻结前从未被写入的行为一致（AddEdge 只写 _pendingEdges）。
    public IReadOnlyCollection<NLCPGEdge> Edges =>
      _queryIndex is null ? Array.Empty<NLCPGEdge>() : _queryIndex.InsertionOrderedEdges;

    internal int CurrentEdgeCount =>
      _queryIndex is null ? _pendingEdges.Count : _queryIndex.EdgeStore.Count;

    // 惰性枚举 pending 边：不分配 PendingEdge[]，供"只扫一遍"的调用者使用。
    // 原先此处是一个 `PendingEdges` 属性，每次访问都全量物化一个 PendingEdge[]（实测 272 B/元素，
    // 在 71 s 快照中分配 641.4 MiB）；调用方 RunInterproceduralDataFlowPass 只需扫一遍建索引，
    // 故改为惰性枚举。需要数组的调用方请用 SnapshotMutableFacts()。
    //
    // ⚠ 只有调用方【不会在枚举期间改图】时才可使用：_keys 是 HashSet，
    //    枚举中修改会抛 InvalidOperationException。
    internal IEnumerable<PendingEdge> EnumeratePendingEdgesLazily() => _pendingEdges.EnumerateLazily(_nodesByOrdinal);

    // scratch 图（worker 局部图）的【具名只读取数通道】。
    //
    // 背景：ControlFlow / Dominance / ControlDependence 三个 pass 各建一张 worker 局部图，
    // 其唯一出口是 LocalCpgFragment（节点描述符 + 边候选）。此前它们经
    // `FreezeQueryIndex()` 后读 `Nodes`/`Edges` 取数，即为这条出口付了整份查询索引的代价
    // （确定性 NodeId、CSR 邻接、按种类分桶、全图 SHA-256），而那份索引随即随图变成垃圾。
    //
    // 为什么必须是【具名】通道，而不是"忘了调用 freeze"：
    //   `Edges` 在未冻结时【静默返回空数组】（见上方 Edges 属性），删除 freeze 而不换数据源
    //   会无声产出空边集（整个阶段的 CFG/支配/控制依赖事实全部丢失）。具名入口把
    //   "这是 scratch 取数"这一意图显式化，并在入口处 fail-closed。
    //
    // 与 EnumeratePendingEdgesLazily 的关键差别（故不可合并）：
    //   持久图的跨过程边【携带 CallSiteContext】，而该通道取的是【原始】ContextId；
    //   冻结路径取的是【已解析】ContextId。两条路径在带调用点上下文时语义分叉，
    //   故本入口对此显式抛错，而 EnumeratePendingEdgesLazily 不得加该守卫
    //   （RunInterproceduralDataFlowPass 正是带 CallSiteContext 的调用方）。
    internal IEnumerable<PendingEdge> EnumerateScratchEdges()
    {
        if (_queryIndex is not null)
        {
            throw new InvalidOperationException(
              "scratch 取数要求图未冻结：冻结时 pending 缓冲已被整体释放（Release），"
              + "且 Edges 会改走查询索引路径。scratch 图应在未冻结状态下经本入口取数。");
        }

        return _pendingEdges.EnumerateScratchEdges(_nodesByOrdinal);
    }

    internal DeterministicNodeIdTable RequirePreallocatedNodeIds()
    {
        return _preallocatedNodeIds ?? throw new InvalidOperationException(
          "Streaming shard publication requires preallocated NodeIds.");
    }

    internal MutableGraphFacts SnapshotMutableFacts()
    {
        // ⚠ 只**读**快照，不是构图——故刻意不走 EnsureMutable 的只读窗口守卫。
        //   否则规划相位内（或在任何只读窗口内）做一次快照会被误报为"分层被违反"，
        //   而真正的问题只是"守卫挂在了读路径上"。仍保留冻结检查：
        //   冻结后 _nodesByOrdinal 已被物化进索引，快照语义不再成立。
        if (_queryIndex is not null)
        {
            throw new InvalidOperationException("The graph is frozen and cannot be mutated.");
        }

        return new MutableGraphFacts(
          _nodesByOrdinal.ToArray(),
          _pendingEdges.Materialize(_nodesByOrdinal));
    }

    public bool HasQueryIndex => _queryIndex is not null;

    internal StringInterner StringTable => _stringInterner;

    internal StableNodeIdentityFactory IdentityFactory => _identityFactory;

    internal bool HasPreallocatedNodeIds => _preallocatedNodeIds is not null;

    public string GraphSnapshotVersion => RequireQueryIndex().SnapshotVersion;

    // 由已冻结的节点和边重建只读查询图。
    public static NLCPGGraph CreateFrozen(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges, StringInterner? stringInterner = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        var graph = new NLCPGGraph(stringInterner: stringInterner);

        // 输入节点只枚举一次，得到一个由索引接管的独占数组（NLCPGGraphIndex.Create 会就地排序它）。
        // 原先这里把每个节点按值写进常驻的 Dictionary<NodeId, NLCPGNode>；现在只需一个临时的
        // NodeId 集合来完成"重复 ID + 边端点已知"两项校验，校验完即可回收。
        var frozenNodes = nodes.ToArray();
        var knownNodeIds = new Dictionary<NodeId, byte>(frozenNodes.Length);
        foreach (var node in frozenNodes)
        {
            if (!node.NodeId.HasValue)
            {
                throw new ArgumentException("Frozen CPG nodes require NodeId values.", nameof(nodes));
            }

            // 与原 _nodesByNodeId.Add 一致：重复 NodeId 抛 ArgumentException（同一框架消息）。
            knownNodeIds.Add(node.NodeId.Value, 0);
        }

        // 局部去重集：原先由常驻的 _edges HashSet 承担（并在冻结后一直被保留），
        // 现改为一次性容器，索引建好后即可回收。这里必须保留一次去重——调用方
        // CpgFrozenShardGraphReader 传入的是未去重的边序列。
        // HashSet 在"只增不删"下按插入序枚举，故这里记录的序即传入序。
        var deduplicatedEdges = new HashSet<NLCPGEdge>();
        foreach (var edge in edges)
        {
            if (!knownNodeIds.ContainsKey(edge.SourceNodeId) ||
                !knownNodeIds.ContainsKey(edge.TargetNodeId))
            {
                throw new ArgumentException("Frozen CPG edges require known endpoints.", nameof(edges));
            }

            deduplicatedEdges.Add(edge);
        }

        graph._queryIndex = NLCPGGraphIndex.Create(frozenNodes, deduplicatedEdges);
        return graph;
    }

    // 向可变图中加入节点，并按稳定锚点做去重合并。
    public NLCPGNode AddNode(NLCPGNode node)
    {
        return AddNode(node, stableIdentityText: null);
    }

    private NLCPGNode AddNode(NLCPGNode node, string? stableIdentityText)
    {
        EnsureMutable();
        var materializedNode = MaterializeCompatibilityIdentity(node, stableIdentityText);
        var stableAnchor = materializedNode.StableAnchor!.Value;
        if (_anchorDiscoveryObserver is not null)
        {
            _anchorDiscoveryObserver(stableAnchor);
            return materializedNode;
        }

        if (!_mutableNodesByAnchor.TryGetValue(stableAnchor, out var existingOrdinal))
        {
            _mutableNodesByAnchor[stableAnchor] = _nodesByOrdinal.Count;
            _nodesByOrdinal.Add(materializedNode);
            return materializedNode;
        }

        var existing = _nodesByOrdinal[existingOrdinal];
        var merged = MergeNode(existing, materializedNode);

        // 【热路径短路】MergeNode 只把 candidate 的【非空】字段盖到 existing 上
        // （`candidate.X == 0 ? existing.X : candidate.X`），故重放一个字段已被 existing
        // 覆盖的 candidate 时，merged 与 existing 逐字段相同，写回是空操作。
        //
        // 发布段每条跨过程边都会走到这里：端点取自已物化的冻结边快照池 ⇒ 锚点必然命中，
        // 于是每条边都付一次 MergeNode 构造（104 B 结构 + 9 个字段的三元选择）与一次
        // List 元素写回。短路后，在"重放无变化"时这些全部省掉。
        //
        // ⚠️ 这里【不能】用 `existing.Equals(merged)`：NLCPGNode.Equals 在两端都带
        // StableAnchor 时【只比 StableAnchor】（NLCPGNode.cs:20-26），而 merged 是
        // `existing with {...}`、锚点必然相同 ⇒ 该调用【恒为 true】，
        // 会把所有真实差异静默丢弃、直接改变图。故必须逐字段比较。
        //
        // 比较的字段集【恰好等于】MergeNode 会覆写的那 9 个：Kind/NodeId/StableAnchor
        // 在 MergeNode 中被原样继承，永远不可能不同，列进来只会误导读者。
        if (!MergeWouldChangeAnyField(existing, merged))
        {
            Interlocked.Increment(ref _redundantMergeSkipCount);
            // 返回 existing（而非 merged）：二者在本分支下逐字段相同，但返回 existing
            // 才能让"没有改动图内节点"与"返回的是图内那个节点"这两件事一致。
            return existing;
        }

        _nodesByOrdinal[existingOrdinal] = merged;
        return merged;
    }

    // MergeNode 是否真的改动了 existing。字段集必须与 MergeNode 的覆写列表逐一对应，
    // 且【不可】复用 NLCPGNode.Equals——后者的 StableAnchor 短路语义不适用于
    // "同一锚点的两个候选谁更完整"这一判断（详见 AddNode 内的注释）。
    private static bool MergeWouldChangeAnyField(NLCPGNode existing, NLCPGNode merged)
    {
        return existing.NameId != merged.NameId ||
          existing.FullNameId != merged.FullNameId ||
          existing.SignatureId != merged.SignatureId ||
          existing.DispatchKind != merged.DispatchKind ||
          existing.TypeFullNameId != merged.TypeFullNameId ||
          existing.FilePathId != merged.FilePathId ||
          existing.SpanStart != merged.SpanStart ||
          existing.SpanEnd != merged.SpanEnd ||
          existing.IsImplicit != merged.IsImplicit;
    }

    // 将只在构图阶段存在的文本草稿物化为图内字符串 ID。
    public NLCPGNode AddNode(NLCPGNodeDraft draft, NodeId? nodeId = null, StableNodeAnchor? stableAnchor = null)
    {
        return AddNode(InternDraft(draft) with
        {
            NodeId = nodeId,
            StableAnchor = stableAnchor,
        }, draft.StableIdentityText);
    }

    private NLCPGNode InternDraft(NLCPGNodeDraft draft)
    {
        return new NLCPGNode(
          draft.Kind,
          NameId: _stringInterner.Intern(draft.Name),
          FullNameId: _stringInterner.Intern(draft.FullName),
          SignatureId: _stringInterner.Intern(draft.Signature),
          DispatchKind: draft.DispatchKind,
          TypeFullNameId: _stringInterner.Intern(draft.TypeFullName),
          FilePathId: _stringInterner.Intern(draft.FilePath),
          SpanStart: draft.SpanStart,
          SpanEnd: draft.SpanEnd,
          IsImplicit: draft.IsImplicit);
    }

    public string ResolveDisplayKind(NLCPGNode node)
    {
        if (node.Kind is NLCPGNodeKind.SyntaxNode or NLCPGNodeKind.SyntaxToken &&
            _identityFactory.TryResolveStableIdentityText(node, out var stableIdentityText) &&
            !string.IsNullOrEmpty(stableIdentityText))
        {
            return stableIdentityText;
        }

        if (node.Kind is NLCPGNodeKind.SyntaxNode or NLCPGNodeKind.SyntaxToken &&
            TryResolveSyntaxDisplayKind(node, out var syntaxKind))
        {
            return syntaxKind;
        }

        return node.Kind.ToString();
    }

    public string? ResolveName(NLCPGNode node)
    {
        return Resolve(node.NameId);
    }

    public string? ResolveFullName(NLCPGNode node)
    {
        return Resolve(node.FullNameId);
    }

    public string? ResolveSignature(NLCPGNode node)
    {
        return Resolve(node.SignatureId);
    }

    public string? ResolveTypeFullName(NLCPGNode node)
    {
        return Resolve(node.TypeFullNameId);
    }

    public string? ResolveFilePath(NLCPGNode node)
    {
        return Resolve(node.FilePathId);
    }

    internal string? Resolve(uint id)
    {
        return _stringInterner.TryResolve(id, out var text) ? text : null;
    }

    private bool TryResolveSyntaxDisplayKind(NLCPGNode node, out string displayKind)
    {
        displayKind = string.Empty;
        var filePath = ResolveFilePath(node);
        if (string.IsNullOrWhiteSpace(filePath) ||
            !node.SpanStart.HasValue ||
            !node.SpanEnd.HasValue ||
            !TryGetSource(filePath, out var source))
        {
            return false;
        }

        var start = node.SpanStart.Value;
        var end = node.SpanEnd.Value;
        if (start < 0 || end < start || end > source.Length)
        {
            return false;
        }

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = syntaxTree.GetRoot();
            if (node.Kind == NLCPGNodeKind.SyntaxToken)
            {
                var token = root.FindToken(start, findInsideTrivia: true);
                if (token.SpanStart == start && token.Span.End == end)
                {
                    displayKind = token.Kind().ToString();
                    return true;
                }

                return false;
            }

            if (_identityFactory.TryResolveStableIdentityText(node, out var stableIdentityText) &&
                !string.IsNullOrEmpty(stableIdentityText))
            {
                foreach (var candidate in root.DescendantNodesAndSelf())
                {
                    if (candidate.SpanStart == start &&
                        candidate.Span.End == end &&
                        string.Equals(candidate.Kind().ToString(), stableIdentityText, StringComparison.Ordinal))
                    {
                        displayKind = stableIdentityText;
                        return true;
                    }
                }
            }

            var syntaxNode = root.FindNode(
              new TextSpan(start, end - start),
              getInnermostNodeForTie: false);
            if (syntaxNode.SpanStart != start || syntaxNode.Span.End != end)
            {
                return false;
            }

            displayKind = syntaxNode.Kind().ToString();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    internal void ImportMutableFacts(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges, StringInterner? sourceStringInterner = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        EnsureMutable();
        var nodesById = new Dictionary<NodeId, NLCPGNode>();
        foreach (var node in nodes)
        {
            if (!node.NodeId.HasValue)
            {
                throw new InvalidDataException("Persisted CPG nodes require NodeIds.");
            }

            var rematerializedNode = node;
            if (sourceStringInterner is not null)
            {
                var draft = new NLCPGNodeDraft(
                  node.Kind,
                  sourceStringInterner.TryResolve(node.NameId, out var name) ? name : null,
                  sourceStringInterner.TryResolve(node.FullNameId, out var fullName) ? fullName : null,
                  sourceStringInterner.TryResolve(node.SignatureId, out var signature) ? signature : null,
                  node.DispatchKind,
                  sourceStringInterner.TryResolve(node.TypeFullNameId, out var typeFullName) ? typeFullName : null,
                  sourceStringInterner.TryResolve(node.FilePathId, out var filePath) ? filePath : null,
                  node.SpanStart,
                  node.SpanEnd,
                  node.IsImplicit);
                rematerializedNode = InternDraft(draft) with
                {
                    NodeId = node.NodeId,
                    StableAnchor = node.StableAnchor,
                };
            }

            nodesById[node.NodeId.Value] = AddNode(rematerializedNode);
        }

        foreach (var edge in edges)
        {
            if (!nodesById.TryGetValue(edge.SourceNodeId, out var source) ||
                !nodesById.TryGetValue(edge.TargetNodeId, out var target))
            {
                throw new InvalidDataException("A persisted CPG edge references a node that was not restored.");
            }

            AddEdge(source, target, edge.Kind, edge.StructuredLabel, edge.ContextId, edge.CallSiteContext);
        }
    }

    // 向可变图加入一条边，并确保端点节点已经物化入图。
    public void AddEdge(NLCPGNode source, NLCPGNode target, NLCPGEdgeKind kind, NLCPGEdgeLabel? structuredLabel = null, NLCPGContextId? contextId = null, NLCPGCallSiteContext? callSiteContext = null)
    {
        EnsureMutable();
        var materializedSource = AddNode(source);
        var materializedTarget = AddNode(target);
        if (_anchorDiscoveryObserver is not null)
        {
            return;
        }

        _pendingEdges.Add(
          ResolveOrdinal(materializedSource),
          ResolveOrdinal(materializedTarget),
          kind,
          structuredLabel,
          contextId,
          callSiteContext);
    }

    // 取节点在构图期的局部序数；序数与锚点一一对应，故等价于按锚点定位节点。
    private int ResolveOrdinal(NLCPGNode node)
    {
        return _mutableNodesByAnchor[node.StableAnchor!.Value];
    }

    public void AddEdge(NLCPGNodeDraft source, NLCPGNodeDraft target, NLCPGEdgeKind kind, NLCPGEdgeLabel? structuredLabel = null, NLCPGContextId? contextId = null, NLCPGCallSiteContext? callSiteContext = null)
    {
        AddEdge(AddNode(source), AddNode(target), kind, structuredLabel, contextId, callSiteContext);
    }

    // 批量提交已由图物化的节点对，避免 overlay 展开时重复执行节点身份合并。
    internal void AddKnownNodeCartesianEdges(
        IReadOnlyList<NLCPGNode> sourceNodes,
        IReadOnlyList<NLCPGNode> targetNodes,
        NLCPGEdgeKind kind)
    {
        EnsureMutable();
        if (_anchorDiscoveryObserver is not null)
        {
            return;
        }

        foreach (var sourceNode in sourceNodes)
        {
            foreach (var targetNode in targetNodes)
            {
                _pendingEdges.Add(
                  ResolveOrdinal(sourceNode),
                  ResolveOrdinal(targetNode),
                  kind,
                  null,
                  null,
                  null);
            }
        }
    }

    // 枚举指定节点种类的所有图节点。
    public IEnumerable<NLCPGNode> NodesByKind(NLCPGNodeKind kind)
    {
        return _queryIndex is null
            ? _nodesByOrdinal.Where(node => node.Kind == kind)
            : GetNodes(kind);
    }

    // 按 NodeId 读取冻结图中的单个节点。
    //
    // 冻结后走索引的 Ordinal 字典；未知 ID 仍返回 null。冻结前图中不存在任何
    // NodeId→节点 的容器（NodeId 只在 AssignDeterministicNodeIds 中才被分配），
    // 故冻结前同样恒返回 null——这是原实现的既有行为，不是本改动的语义变更。
    public NLCPGNode? GetNode(NodeId nodeId)
    {
        if (_queryIndex is not null)
        {
            return _queryIndex.TryGetNode(nodeId, out var frozenNode) ? frozenNode : null;
        }

        return null;
    }

    // 记录源码文本，供节点展示时按跨度回切原文。
    public void RegisterSource(string filePath, string source)
    {
        EnsureMutable();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _sourceByPath[filePath] = source;

        var fullPath = Path.GetFullPath(filePath);
        if (!string.Equals(fullPath, filePath, StringComparison.Ordinal))
        {
            _sourceByPath[fullPath] = source;
        }
    }

    // 返回节点的展示文本，优先使用源码切片，再回退到名称。
    public string GetDisplayText(NLCPGNode node)
    {
        if (TryResolveSourceSlice(node, out var sourceText))
        {
            return sourceText;
        }

        return ResolveFullName(node) ?? ResolveName(node) ?? ResolveDisplayKind(node);
    }

    // 为当前图分配确定性 NodeId 并生成只读查询索引。
    public void FreezeQueryIndex()
    {
        if (_queryIndex is not null)
        {
            return;
        }

        var frozen = AssignDeterministicNodeIds();
        _queryIndex = NLCPGGraphIndex.Create(frozen.Nodes, frozen.Edges);
        // 放在 _queryIndex 赋值【之后】：回收依赖"图已进入查询态"这一前提
        // （Nodes / NodesByKind / EnsureMutable 都按 _queryIndex 是否为空分流），
        // 若提前到 AssignDeterministicNodeIds 内部，会出现"缓冲已释放但图仍被当作可变"
        // 的窗口，此时读取 pending 边会走进 EnsureMutable 的可变分支而看不到保护性异常。
        ReclaimConstructionCapacity();
    }

    // 返回指定节点发出的全部边。
    public IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: true);
    }

    // 返回流入指定节点的全部边。
    public IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: false);
    }

    // 返回流入指定节点且边种类匹配的边。
    public IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetIncomingEdges(nodeId, kind);
    }

    // 返回由指定节点发出且边种类匹配的边。
    public IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetOutgoingEdges(nodeId, kind);
    }

    // 返回指定边种类的全部边。
    public IReadOnlyList<NLCPGEdge> GetEdges(NLCPGEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetEdges(kind);
    }

    // 返回指定节点种类的全部冻结节点。
    public IReadOnlyList<NLCPGNode> GetNodes(NLCPGNodeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetNodesByKind(kind);
    }

    // 返回引用指定符号节点的全部图节点。
    public IReadOnlyList<NLCPGNode> GetSymbolReferences(NodeId symbolNodeId)
    {
        var index = RequireQueryIndex();
        return GetIncomingEdges(symbolNodeId, NLCPGEdgeKind.Ref)
            .Select(edge => index.GetKnownNode(edge.SourceNodeId))
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    // 返回由指定方法节点拥有的调用点节点。
    public IReadOnlyList<NLCPGNode> GetMethodOwnedCallSites(NodeId methodNodeId)
    {
        var index = RequireQueryIndex();
        return GetOutgoingEdges(methodNodeId, NLCPGEdgeKind.ContainsSymbol)
            .Select(edge => index.GetKnownNode(edge.TargetNodeId))
            .Where(node => node.Kind == NLCPGNodeKind.CallSite)
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    // 返回完全落在指定文件跨度内的节点。
    public IReadOnlyList<NLCPGNode> GetNodesInFileSpan(string filePath, int start, int end)
    {
        if (start < 0 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The span must be a valid half-open interval.");
        }

        var index = RequireQueryIndex();
        var filePathId = _stringInterner.Intern(filePath);
        if (!index.TryGetNodesByFilePath(filePathId, out var nodes))
        {
            return Array.Empty<NLCPGNode>();
        }

        return nodes.Where(node => node.SpanStart >= start && node.SpanEnd <= end)
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    // 为一组边种类计算稳定的掩码哈希。
    public int GetEdgeMaskId(IReadOnlySet<NLCPGEdgeKind> edgeKinds)
    {
        ArgumentNullException.ThrowIfNull(edgeKinds);
        var hash = new HashCode();
        foreach (var kind in edgeKinds.OrderBy(kind => kind))
        {
            hash.Add((int)kind);
        }

        return hash.ToHashCode();
    }

    // 返回当前节点发出的控制依赖边。
    public IReadOnlyList<NLCPGEdge> Controls(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.ControlDependence)
            .ToArray();
    }

    // 返回控制当前节点的控制依赖边。
    public IReadOnlyList<NLCPGEdge> ControlledBy(NodeId nodeId)
    {
        return GetIncomingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.ControlDependence)
            .ToArray();
    }

    // 返回当前节点支配到的节点边。
    public IReadOnlyList<NLCPGEdge> Dominates(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.Dominates)
            .ToArray();
    }

    // 返回当前节点后支配到的节点边。
    public IReadOnlyList<NLCPGEdge> PostDominates(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.PostDominates)
            .ToArray();
    }

    // 从锚点出发按跳数与方向抽取局部子图视图。
    public NLCPGLocalView ExtractLocalView(NodeId anchorNodeId, int hops, NLCPGViewDirection direction = NLCPGViewDirection.Both, IReadOnlyCollection<NLCPGEdgeKind>? edgeKinds = null)
    {
        if (hops < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hops), "Hops must be non-negative.");
        }

        var index = RequireQueryIndex();
        if (!index.TryGetNode(anchorNodeId, out var anchor))
        {
            throw new ArgumentException($"Unknown anchor node id: {anchorNodeId}", nameof(anchorNodeId));
        }

        var allowedKinds = edgeKinds is null ? null : new HashSet<NLCPGEdgeKind>(edgeKinds);
        var visitedNodeIds = new HashSet<NodeId> { anchorNodeId };
        var frontierNodeIds = new HashSet<NodeId> { anchorNodeId };

        for (var depth = 0; depth < hops; depth += 1)
        {
            var nextFrontierNodeIds = new HashSet<NodeId>();
            foreach (var nodeId in frontierNodeIds)
            {
                ExpandFrom(nodeId, direction, index, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
            }

            frontierNodeIds = nextFrontierNodeIds;
            if (frontierNodeIds.Count == 0)
            {
                break;
            }
        }

        var localNodes = visitedNodeIds
          .Select(index.GetKnownNode)
          .OrderBy(node => node.NodeId)
          .ToArray();
        var localEdges = index.OrderedEdges
          .Where(edge =>
            (allowedKinds is null || allowedKinds.Contains(edge.Kind)) &&
            visitedNodeIds.Contains(edge.SourceNodeId) &&
            visitedNodeIds.Contains(edge.TargetNodeId))
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind)
          .ThenBy(edge => edge.TargetNodeId)
          .ToArray();
        return new NLCPGLocalView(anchor, hops, localNodes, localEdges);
    }

    private static void ExpandFrom(NodeId nodeId, NLCPGViewDirection direction, NLCPGGraphIndex index, HashSet<NLCPGEdgeKind>? allowedKinds, ISet<NodeId> visitedNodeIds, ISet<NodeId> nextFrontierNodeIds)
    {
        if (direction is NLCPGViewDirection.Both or NLCPGViewDirection.Outgoing)
        {
            ExpandNeighbors(nodeId, index.GetOutgoingEdges(nodeId), useOutgoingTarget: true, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
        }

        if (direction is NLCPGViewDirection.Both or NLCPGViewDirection.Incoming)
        {
            ExpandNeighbors(nodeId, index.GetIncomingEdges(nodeId), useOutgoingTarget: false, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
        }
    }

    private static void ExpandNeighbors(NodeId nodeId, IReadOnlyList<NLCPGEdge> edges, bool useOutgoingTarget, HashSet<NLCPGEdgeKind>? allowedKinds, ISet<NodeId> visitedNodeIds, ISet<NodeId> nextFrontierNodeIds)
    {
        foreach (var edge in edges)
        {
            if (allowedKinds is not null && !allowedKinds.Contains(edge.Kind))
            {
                continue;
            }

            var neighborId = useOutgoingTarget ? edge.TargetNodeId : edge.SourceNodeId;
            if (visitedNodeIds.Add(neighborId))
            {
                nextFrontierNodeIds.Add(neighborId);
            }
        }
    }

    private IReadOnlyList<NLCPGEdge> GetAdjacency(NodeId nodeId, bool useOutgoingEdges)
    {
        var index = RequireQueryIndex();
        return useOutgoingEdges ? index.GetOutgoingEdges(nodeId) : index.GetIncomingEdges(nodeId);
    }

    private NLCPGGraphIndex RequireQueryIndex()
    {
        return _queryIndex ?? throw new InvalidOperationException("The graph query index is unavailable until the graph has been frozen.");
    }

    private bool TryResolveSourceSlice(NLCPGNode node, out string text)
    {
        text = string.Empty;
        var filePath = ResolveFilePath(node);
        if (string.IsNullOrWhiteSpace(filePath) ||
            !node.SpanStart.HasValue ||
            !node.SpanEnd.HasValue)
        {
            return false;
        }

        if (!TryGetSource(filePath, out var source))
        {
            return false;
        }

        var start = node.SpanStart.Value;
        var end = node.SpanEnd.Value;
        if (start < 0 || end < start || end > source.Length)
        {
            return false;
        }

        text = source[start..end];
        return true;
    }

    private bool TryGetSource(string filePath, out string source)
    {
        if (_sourceByPath.TryGetValue(filePath, out source!))
        {
            return true;
        }

        var fullPath = Path.GetFullPath(filePath);
        return _sourceByPath.TryGetValue(fullPath, out source!);
    }

    private NLCPGNode MaterializeCompatibilityIdentity(NLCPGNode node, string? stableIdentityText)
    {
        var stableAnchor = _identityFactory.GetStableAnchor(node, _stringInterner, stableIdentityText);
        if (_preallocatedNodeIds is not null)
        {
            if (!_preallocatedNodeIds.Contains(stableAnchor))
            {
                throw new InvalidOperationException(
                    $"Stable anchor for '{DescribeNode(node)}' was not included in the supplied preallocated NodeId table.");
            }

            var nodeId = _preallocatedNodeIds.GetRequiredId(stableAnchor);
            if (node.NodeId.HasValue && node.NodeId.Value != nodeId)
            {
                throw new InvalidOperationException(
                    $"Preallocated NodeId for '{DescribeNode(node)}' does not match the supplied NodeId.");
            }

            return node with
            {
                NodeId = nodeId,
                StableAnchor = stableAnchor,
            };
        }

        if (node.NodeId.HasValue && node.StableAnchor.HasValue)
        {
            return node;
        }

        return node with
        {
            NodeId = node.NodeId,
            StableAnchor = stableAnchor,
        };
    }

    private static NLCPGNode MergeNode(NLCPGNode existing, NLCPGNode candidate)
    {
        return existing with
        {
            NameId = candidate.NameId == 0 ? existing.NameId : candidate.NameId,
            FullNameId = candidate.FullNameId == 0 ? existing.FullNameId : candidate.FullNameId,
            SignatureId = candidate.SignatureId == 0 ? existing.SignatureId : candidate.SignatureId,
            DispatchKind = candidate.DispatchKind ?? existing.DispatchKind,
            TypeFullNameId = candidate.TypeFullNameId == 0 ? existing.TypeFullNameId : candidate.TypeFullNameId,
            FilePathId = candidate.FilePathId == 0 ? existing.FilePathId : candidate.FilePathId,
            SpanStart = candidate.SpanStart ?? existing.SpanStart,
            SpanEnd = candidate.SpanEnd ?? existing.SpanEnd,
            IsImplicit = existing.IsImplicit || candidate.IsImplicit,
        };
    }

    // 为构图期节点分配确定性 NodeId，并把节点物化成一份【由索引接管】的独占数组。
    //
    // 返回的 Nodes 数组的枚举序 = _nodesByOrdinal 的序数序（即首次入图序），与原先写入
    // _nodesByNodeId 后由 Dictionary 枚举出的顺序一致；NLCPGGraphIndex.Create 会就地按
    // NodeId 排序它，并另存一份"输入序 -> canonical 序"的 4 B/节点排列。
    private (NLCPGNode[] Nodes, NLCPGEdge[] Edges) AssignDeterministicNodeIds()
    {
        var anchoredNodes = _nodesByOrdinal
          .Select(node =>
          {
              var anchor = _identityFactory.GetStableAnchor(node, _stringInterner);
              return (Node: node, Anchor: anchor);
          })
          .ToArray();
        var nodeIdTable = _preallocatedNodeIds ?? DeterministicNodeIdTable.Create(anchoredNodes.Select(entry => entry.Anchor));
        if (_preallocatedNodeIds is not null &&
            (_preallocatedNodeIds.Count != anchoredNodes.Length ||
             anchoredNodes.Any(entry => !_preallocatedNodeIds.Contains(entry.Anchor))))
        {
            throw new InvalidOperationException("The supplied preallocated NodeId table must exactly match the graph's stable anchors.");
        }

        // 按序数索引的重映射结果：frozenNodes[ordinal] / nodeIdByOrdinal[ordinal] 即该序数
        // 对应节点的确定性 NodeId。
        //
        // 原先这里是 `anchoredNodes.Select(...).ToDictionary(node => node.StableAnchor!.Value)`，
        // 再由字典反查两次。锚点与序数一一对应（AddNode 以锚点为键，每个锚点只
        // _nodesByOrdinal.Add 一次），故直接用序数下标即可，无需该字典：它的 entry 内联
        // NLCPGNode（104 B），是冻结峰值上的一个 nodeCount 级容器。
        var frozenNodes = new NLCPGNode[anchoredNodes.Length];
        var nodeIdByOrdinal = new NodeId[anchoredNodes.Length];
        for (var ordinal = 0; ordinal < anchoredNodes.Length; ordinal += 1)
        {
            var entry = anchoredNodes[ordinal];
            if (!nodeIdTable.TryGetNodeId(entry.Anchor, out var nodeId))
            {
                throw new InvalidOperationException($"Failed to resolve deterministic NodeId for '{DescribeNode(entry.Node)}'.");
            }

            frozenNodes[ordinal] = entry.Node with
            {
                NodeId = nodeId,
                StableAnchor = entry.Anchor,
            };
            nodeIdByOrdinal[ordinal] = nodeId;
        }

        // 直接按序填充预分配数组，不经 PendingEdge[] / 字典 / HashSet 中转。
        //
        // 原先这里是 `_pendingEdges.Materialize(...).Select(...).ToArray()`：Materialize 会先分配
        // 一个 PendingEdge[]（实测 272 B/元素），而 Select 是惰性的，故该数组在整个投影期间
        // 一直存活，与新分配的 NLCPGEdge[]（72 B/元素）同时占用。以 NPC.cs（20,723,806 边）为例，
        // 仅 PendingEdge[] 就 5,376 MB，是峰值的主要来源之一。
        //
        // 其后一度改为逐条 Add 进 HashSet<NLCPGEdge> 以求去重。该去重是**可证的冗余**：
        // _pendingEdges 内部已用 HashSet<PendingEdgeKey> 按值去重，而
        // PendingEdgeKey → NLCPGEdge 的映射是单射（序数→锚点双射、锚点→NodeId 由
        // DeterministicNodeIdTable.Distinct 后逐个赋 index+1 保证单射、Kind 直传、
        // MetadataId 同一 id 即同一元数据实例），故两条不同键必得两条不同边。
        // 实测佐证：NPC.cs 枚举 7,756,984 条边，HashSet 丢弃数为 0。
        // 详见 设计docs/历史设计/2026-09-24-AssignDeterministicNodeIds-边重映射去哈希化设计.md
        //
        // EnumerateOrdinalsLazily 与 Materialize/EnumerateLazily 枚举同一 _keys、顺序完全一致，
        // 但只给序数而非两端节点；调用方是"扫一遍",且此处不会改图，符合其使用前提。
        var remappedEdges = RemapEdgesOrdinal(_pendingEdges, nodeIdByOrdinal);

        _mutableNodesByAnchor.Clear();
        _nodesByOrdinal.Clear();

        return (frozenNodes, remappedEdges);
    }

    // 新实现：预分配数组 + 序数下标，零哈希。
    private NLCPGEdge[] RemapEdgesOrdinal(
      PendingEdgeBuffer pendingEdges,
      NodeId[] nodeIdByOrdinal)
    {
        var remappedEdges = new NLCPGEdge[pendingEdges.Count];
        var written = 0;
        foreach (var edge in pendingEdges.EnumerateOrdinalsLazily())
        {
            // 用 CreateProjected 而非公开构造函数：ContextId 已在 InternMetadata 里
            // 按元数据解析并随元数据条目共享，这里再走公开构造函数就会为每条边重新
            // 插值一遍 callsite: 字符串，把"每元数据一次"重新放大成"每边一次"
            // （NPC.cs 实测 3,708,435 个实例 vs 2,020 个不同内容，峰值 1,290.0 MB）。
            // 投影出的字段值与公开构造函数的结果逐字段相同。
            remappedEdges[written] = NLCPGEdge.CreateProjected(
              nodeIdByOrdinal[edge.SourceOrdinal],
              nodeIdByOrdinal[edge.TargetOrdinal],
              edge.Kind,
              edge.StructuredLabel,
              edge.ContextId,
              edge.CallSiteContext);
            written += 1;
        }

        if (written != remappedEdges.Length)
        {
            throw new InvalidOperationException(
              $"Pending edge enumeration yielded {written} edges but the buffer reported {remappedEdges.Length}.");
        }

        return remappedEdges;
    }

    // 冻结后回收仅供构图使用的空容器容量。
    //
    // Clear() 只把 Count 归零，底层桶/元素数组仍保持峰值容量。冻结是这些容器的最后一次
    // 写入点，此后只读，故回收安全。在 NPC.cs 规模（1,722,521 节点 / 20,723,806 边）下，
    // 这些"已清空但仍占容量"的容器合计约 869 MiB。
    //
    // 【只回收空容器】：TrimExcess() 对非空容器是"分配等长新数组 → 复制 → 换字段"，
    // 期间新旧两份数组同时存活（独立进程实测，避免 PeakWorkingSet 单调高水位互相掩盖）：
    // 原先常驻的 _edges（20.7M 元素）回收 249.8 MiB 却新增分配 1,660.2 MiB、峰值 +1,010 MiB、
    // 耗时 2,802 ms；当时的非空 _nodesByNodeId 回收 134.0 MiB 而新增分配 222.3 MiB、峰值 +184 MiB。
    // 本系统私有峰值已达 21,551 MB = 1.52× 物理内存，用这种瞬时峰值换稳态收益会把
    // 内存不足的风险放大，故这里只对【已为空】的容器做 TrimExcess。
    // 对空容器而言 TrimExcess 只是把内部数组换成空数组，O(1) 且无大分配（实测 0~1 ms、0 新增分配）。
    //
    // 注：_edges 已在冻结时不再常驻（边改由 NLCPGGraphIndex 的投影视图提供），
    // 上面的 _edges / _nodesByNodeId 实测只作为"为何不对非空容器 TrimExcess"的历史依据保留；
    // _nodesByNodeId 本身已整体移除（见下）。
    private void ReclaimConstructionCapacity()
    {
        // 构图期锚点索引与局部序数表：冻结后不再被读取
        // （Nodes / NodesByKind 在 _queryIndex 非空时均改走查询索引），故容量可全部归还。
        //
        // _nodesByNodeId（Dictionary<NodeId, NLCPGNode>）已整体移除而非 Clear：它的 entry 按值
        // 内联 104 B 的 NLCPGNode，是本子系统原先最大的一项常驻载荷；改由索引的
        // NodeId→ordinal 字典承担按 ID 读取，不再需要第二份完整节点容器。
        _mutableNodesByAnchor.TrimExcess();
        _nodesByOrdinal.TrimExcess();
        // 构图期的 pending 边缓冲在 FreezeQueryIndex 之前已被 RunInterproceduralDataFlowPass
        // 消费完（NLCPGBuilder 中 L385 先于 L390），故此处整体释放而非仅收缩。
        _pendingEdges.Release();
    }

    private string DescribeNode(NLCPGNode node)
    {
        return ResolveFullName(node) ??
          ResolveName(node) ??
          $"{node.Kind}:{ResolveFilePath(node)}:{node.SpanStart}:{node.SpanEnd}";
    }

    private static StableNodeRole MapStableNodeRole(NLCPGNodeKind kind)
    {
        return kind switch
        {
            NLCPGNodeKind.SyntaxNode => StableNodeRole.SyntaxNode,
            NLCPGNodeKind.SyntaxToken => StableNodeRole.SyntaxToken,
            NLCPGNodeKind.Operation => StableNodeRole.Operation,
            NLCPGNodeKind.Reference => StableNodeRole.Reference,
            NLCPGNodeKind.TypeRef => StableNodeRole.TypeReference,
            NLCPGNodeKind.TypeDecl => StableNodeRole.TypeDeclaration,
            NLCPGNodeKind.Method => StableNodeRole.Method,
            NLCPGNodeKind.MethodParameter => StableNodeRole.MethodParameter,
            NLCPGNodeKind.MethodReturn => StableNodeRole.MethodReturn,
            NLCPGNodeKind.MethodEntry => StableNodeRole.MethodEntry,
            NLCPGNodeKind.MethodExit => StableNodeRole.MethodExit,
            NLCPGNodeKind.CallSite => StableNodeRole.CallSite,
            NLCPGNodeKind.MemberAccess => StableNodeRole.MemberAccess,
            NLCPGNodeKind.SymbolMethod or
            NLCPGNodeKind.SymbolParameter or
            NLCPGNodeKind.SymbolLocal or
            NLCPGNodeKind.SymbolField or
            NLCPGNodeKind.SymbolProperty or
            NLCPGNodeKind.SymbolType or
            NLCPGNodeKind.SymbolUnknown => StableNodeRole.Symbol,
            _ => StableNodeRole.None,
        };
    }

    private void EnsureMutable()
    {
        if (_queryIndex is not null)
        {
            throw new InvalidOperationException("The graph is frozen and cannot be mutated.");
        }

        // G0-P 附录 R.3：L0 规划层必须只读。放在本方法内是刻意的——它是**全部**构图入口
        // （AddNode/AddEdge/AddKnownNodeCartesianEdges/ImportMutableFacts/RegisterSource）
        // 共用的唯一收口，故守卫不可能被某个新入口绕过。
        if (_readOnlyWindowDepth > 0)
        {
            throw new InvalidOperationException(
              "G0-P 附录 R.3 分层被违反：只读窗口（L0 规划层）内发生了构图操作。"
              + "规划相位只允许读语法/语义模型与既有缓存并产出 plan，"
              + "写图是 L2 归并层（Commit*/reducer）的职责。"
              + "若确实需要在此写图，说明该阶段**不能**在窗口内规划——"
              + "应像 ControlDependence 那样如实声明 DeferredUntilRuntimeInputs，"
              + "而不是扩大只读窗口。");
        }

        // G0-P 附录 R.3：L1 计算层必须"只算 fragment，不写共享图"。同样放在本收口内，
        // 理由与 L0 一致：它是全部构图入口共用的唯一通道，新入口不可能绕过。
        //
        // ⚠ 只拦【本图】的写：worker 会另建私有 localGraph（ControlFlow/ControlDependence/
        //   Dominance），那是 fragment 的构造过程，属 L1 的**职责**，不受本守卫约束。
        //   由于计数按实例存放，localGraph 自身深度为 0，天然放行——不需特判。
        if (_workerComputeWindowDepth.Value > 0)
        {
            throw new InvalidOperationException(
              "G0-P 附录 R.3 分层被违反：worker 计算窗口（L1 计算层）内发生了对共享图的构图操作。"
              + "L1 只允许读取既有节点/语义模型并产出 fragment（新建私有 localGraph 是允许的），"
              + "写共享图是 L2 归并层（Commit*/reducer）的职责。"
              + "若确实需要在此写图，应把写操作移到 reduceResult 回调中，"
              + "而不是在 worker 内直接改图——后者会与并发 worker 竞争同一图结构。");
        }
    }

    // 以"两端节点序数 + 边种类 + 元数据 id"去重，延迟创建 PendingEdge 对象到需要读取或冻结时。
    //
    // 内存设计（实测）：键 = 2 个 int 序数 + kind + int 元数据 id ⇒ 16 B 载荷，
    // HashSet Entry 槽 24 B（+4 B 桶）＝ 28 B/边；
    // 旧实现两端各存 28 B 锚点，且键与明细各存一份 120 B 载荷 ⇒ 140 B/边。
    // 元数据以 id 参与哈希与相等性，故去重语义与旧实现完全一致：
    // 两端相同、kind 相同但元数据不同的边仍是两条边（不依赖"元数据由 kind 决定"这一未证前提）。
    private sealed class PendingEdgeBuffer
    {
        // 元数据去重池：相同元数据只登记一次，键中只存 4 B id。
        // _metadataById[0] 固定为 null（"无元数据"哨兵），故 id 0 表示无元数据。
        private readonly Dictionary<EdgeMetadata, int> _metadataPool = new();
        private readonly List<EdgeMetadata?> _metadataById = new() { null };

        // 与 _metadataById 同下标的**已解析** ContextId，[0] 同样是 null 哨兵。
        //
        // 为什么单独立一张表而不改写 _metadataById 里的 ContextId：
        //   · Materialize / EnumerateLazily 的消费方（PendingEdge → CpgEdgeCandidate →
        //     分片协调器 edge.ContextId?.Value 落盘、SkeletonShardPublisher 的
        //     ThenBy(edge.ContextId) 排序）此前拿到的是**原始**（生产侧恒为 null）值，
        //     把它换成已解析值会改变落盘内容与边界分片排序，属于语义变化。
        //   · EnumerateOrdinalsLazily 的唯一消费方是 RemapEdgesOrdinal，它此前走
        //     NLCPGEdge 公开构造函数，**本来就会**把 ContextId 解析出来。
        // 故只有序数路径改读本表，其余路径逐字节不变。
        private readonly List<NLCPGContextId?> _resolvedContextIdById = new() { null };
        private readonly HashSet<PendingEdgeKey> _keys = new();
        // 冻结后置位：缓冲已整体释放，此后任何读取都是调用方用错生命周期。
        // 必须显式抛错而不是静默返回空集合——静默返回会让"冻结后仍取 pending 边"
        // 这类 bug 表现为空图，而不是立刻失败。
        private bool _released;

        internal int Count
        {
            get
            {
                ThrowIfReleased();
                return _keys.Count;
            }
        }

        internal void Add(
          int sourceOrdinal,
          int targetOrdinal,
          NLCPGEdgeKind kind,
          NLCPGEdgeLabel? structuredLabel,
          NLCPGContextId? contextId,
          NLCPGCallSiteContext? callSiteContext)
        {
            ThrowIfReleased();
            var metadataId = InternMetadata(structuredLabel, contextId, callSiteContext);
            // HashSet.Add 以一次查找同时完成"判重 + 登记"。
            _keys.Add(new PendingEdgeKey(sourceOrdinal, targetOrdinal, kind, metadataId));
        }

        // 冻结后整体释放构图期缓冲（键集合、元数据池与元数据表）。
        // 与 TrimExcess 不同，这里连元素也一并丢弃：NLCPGBuilder 在 FreezeQueryIndex 之前
        // 已用 RunInterproceduralDataFlowPass 消费完 pending 边，冻结后无人再读。
        internal void Release()
        {
            ThrowIfReleased();
            _released = true;
            _keys.Clear();
            _keys.TrimExcess();
            _metadataPool.Clear();
            _metadataPool.TrimExcess();
            // 保留 _metadataById[0] 的 null 哨兵，使 id 0 == "无元数据" 这一不变量在释放后仍成立。
            _metadataById.Clear();
            _metadataById.Add(null);
            _metadataById.TrimExcess();
            _resolvedContextIdById.Clear();
            _resolvedContextIdById.Add(null);
            _resolvedContextIdById.TrimExcess();
        }

        private void ThrowIfReleased()
        {
            if (_released)
            {
                throw new InvalidOperationException(
                  "The pending-edge buffer was released at freeze time and can no longer be read.");
            }
        }

        // 返回元数据的稳定 id；无元数据时恒为 0（_metadataById[0] 是 null 哨兵）。
        //
        // ContextId 在这里解析**一次**并随条目共享。生产侧所有边都以
        // contextId: null + 真实 CallSiteContext 入池，故此前池里存的是 null，
        // 于是每一条边都要在投影时各自插值一遍 callsite: 字符串：
        // NPC.cs 实测 7,756,984 条边只有 2,020 个不同内容，却产生 3,708,435 个
        // 互不相同的字符串实例（1,835.9× 冗余），峰值 1,290.0 MB。
        // 解析移到去重命中之后，插值次数由"边数"降为"不同元数据数"。
        private int InternMetadata(
          NLCPGEdgeLabel? structuredLabel,
          NLCPGContextId? contextId,
          NLCPGCallSiteContext? callSiteContext)
        {
            if (structuredLabel is null && contextId is null && callSiteContext is null)
            {
                return 0;
            }

            var candidate = new EdgeMetadata(structuredLabel, contextId, callSiteContext);
            if (_metadataPool.TryGetValue(candidate, out var existingId))
            {
                return existingId;
            }

            // 与 NLCPGEdge 公开构造函数同一条解析规则：调用点存在时它优先，
            // 且必须与显式提供的 contextId 一致（不一致时构造函数会抛错，这里同样校验，
            // 以免同一非法组合在构图期被静默接受、到投影时才炸）。
            var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
            if (callSiteContext.HasValue &&
                contextId.HasValue &&
                contextId.Value != resolvedContextId)
            {
                throw new ArgumentException(
                  "CallSiteContext must derive the same ContextId when both are provided.");
            }

            var id = _metadataById.Count;
            _metadataById.Add(candidate);
            _resolvedContextIdById.Add(resolvedContextId);
            _metadataPool[candidate] = id;
            return id;
        }

        internal IReadOnlyList<PendingEdge> Materialize(IReadOnlyList<NLCPGNode> nodesByOrdinal)
        {
            ThrowIfReleased();
            var pendingEdges = new PendingEdge[_keys.Count];
            var index = 0;
            // HashSet 在"只增不删"下按插入序枚举（已实测，且与字符串哈希随机化无关）。
            foreach (var key in _keys)
            {
                // id 0 是 null 哨兵（无元数据）。
                var metadata = _metadataById[key.MetadataId];
                pendingEdges[index] = new PendingEdge(
                  nodesByOrdinal[key.SourceOrdinal],
                  nodesByOrdinal[key.TargetOrdinal],
                  key.Kind,
                  metadata?.StructuredLabel,
                  metadata?.ContextId,
                  metadata?.CallSiteContext);
                index += 1;
            }

            return pendingEdges;
        }

        // 惰性枚举同一序列，但【不分配 PendingEdge[]】。
        // 只给"扫一遍建索引"的调用者用；枚举顺序与 Materialize 完全一致（同一个 _keys HashSet）。
        // 若调用者还需要随机访问或复用，请改用 Materialize。
        internal IEnumerable<PendingEdge> EnumerateLazily(IReadOnlyList<NLCPGNode> nodesByOrdinal)
        {
            // 守卫放在非迭代器的外层方法里：若直接写进下面的 yield 体，异常会推迟到首次
            // MoveNext 才抛出，调用点将看不到失败。
            ThrowIfReleased();
            return EnumerateLazilyCore(nodesByOrdinal);
        }

        private IEnumerable<PendingEdge> EnumerateLazilyCore(IReadOnlyList<NLCPGNode> nodesByOrdinal)
        {
            foreach (var key in _keys)
            {
                var metadata = _metadataById[key.MetadataId];
                yield return new PendingEdge(
                  nodesByOrdinal[key.SourceOrdinal],
                  nodesByOrdinal[key.TargetOrdinal],
                  key.Kind,
                  metadata?.StructuredLabel,
                  metadata?.ContextId,
                  metadata?.CallSiteContext);
            }
        }

        // scratch 取数：与 EnumerateLazily 同一 _keys、同一顺序，但额外守卫
        // "scratch 边不得携带调用点上下文"。
        //
        // 为什么这条守卫必须存在（否则是一个【静默】差异）：
        //   · 冻结路径经 EnumerateOrdinalsLazily 取的是【已解析】ContextId
        //     （_resolvedContextIdById[MetadataId]）；
        //   · pending 路径经 EnumerateLazily 取的是【原始】ContextId（metadata?.ContextId）。
        //   解析规则是 `callSiteContext?.ToContextId() ?? contextId` ⇒ **只有 CallSiteContext
        //   非空时两路才分叉**（冻结路径给出插值出的非 null 值，pending 路径给出原始值/null）；
        //   而仅有显式 ContextId 时两路逐字段相同（resolved == 原始值）。
        //   生产侧 scratch 边的元数据恒为 null（四类发射点均 3 参 AddEdge 或显式 null），
        //   故当前两路都给出 null。但若将来有人给 scratch 边挂上 CallSiteContext，
        //   冻结路径会给出【非 null 已解析值】、pending 路径给出 null，不抛异常、不报错。
        //   判据刻意只认 CallSiteContext，以免把"其实不发散"的显式 ContextId 也一并误拒。
        internal IEnumerable<PendingEdge> EnumerateScratchEdges(IReadOnlyList<NLCPGNode> nodesByOrdinal)
        {
            ThrowIfReleased();
            return EnumerateScratchEdgesCore(nodesByOrdinal);
        }

        private IEnumerable<PendingEdge> EnumerateScratchEdgesCore(IReadOnlyList<NLCPGNode> nodesByOrdinal)
        {
            foreach (var key in _keys)
            {
                var metadata = _metadataById[key.MetadataId];
                if (metadata?.CallSiteContext is not null)
                {
                    throw new InvalidOperationException(
                      "scratch 边不得携带 CallSiteContext：scratch 取数通道给出的是【原始】ContextId，"
                      + "而冻结路径给出 ToContextId() 解析后的值，两者在携带 CallSiteContext 时分叉。"
                      + "若确需带调用点上下文，请改走 FreezeQueryIndex + Edges 路径。");
                }

                yield return new PendingEdge(
                  nodesByOrdinal[key.SourceOrdinal],
                  nodesByOrdinal[key.TargetOrdinal],
                  key.Kind,
                  metadata?.StructuredLabel,
                  metadata?.ContextId,
                  metadata?.CallSiteContext);
            }
        }

        // 只暴露序数的惰性枚举：与 EnumerateLazily 枚举同一 _keys、顺序完全一致，
        // 但不把两端节点（各 104 B）复制出来，调用方可直接用序数索引自己的数组。
        // 给"冻结时重映射边"这一唯一调用者用。
        internal IEnumerable<PendingEdgeOrdinal> EnumerateOrdinalsLazily()
        {
            ThrowIfReleased();
            return EnumerateOrdinalsLazilyCore();
        }

        private IEnumerable<PendingEdgeOrdinal> EnumerateOrdinalsLazilyCore()
        {
            foreach (var key in _keys)
            {
                var metadata = _metadataById[key.MetadataId];
                // ContextId 取【已解析】值：本枚举的唯一消费方 RemapEdgesOrdinal 投影出的边，
                // 其 ContextId 语义就是"公开构造函数解析后的值"。这里直接给共享实例，
                // 避免每条边重新插值一次 callsite: 字符串。
                yield return new PendingEdgeOrdinal(
                  key.SourceOrdinal,
                  key.TargetOrdinal,
                  key.Kind,
                  metadata?.StructuredLabel,
                  _resolvedContextIdById[key.MetadataId],
                  metadata?.CallSiteContext);
            }
        }

        // 键：两个节点序数 + 边种类 + 元数据 id。全部为值类型，不含 string/引用 ⇒ 枚举序跨进程稳定。
        private readonly record struct PendingEdgeKey(
          int SourceOrdinal,
          int TargetOrdinal,
          NLCPGEdgeKind Kind,
          int MetadataId);

        // 元数据三元组，按值比较（record class 自动生成 Equals/GetHashCode）。
        // 用 class 而非 struct：作为字典键时 struct 会把 32 B 载荷内联进槽位，
        // 实测令 f=6.25% 下的池开销从 4.5 MiB 涨到 16.5 MiB。
        private sealed record EdgeMetadata(
          NLCPGEdgeLabel? StructuredLabel,
          NLCPGContextId? ContextId,
          NLCPGCallSiteContext? CallSiteContext);
    }

    internal readonly record struct PendingEdge(
      NLCPGNode SourceNode,
      NLCPGNode TargetNode,
      NLCPGEdgeKind Kind,
      NLCPGEdgeLabel? StructuredLabel,
      NLCPGContextId? ContextId,
      NLCPGCallSiteContext? CallSiteContext);

    // 与 PendingEdge 同序同内容，但两端只给序数（各 4 B）而非节点（各 104 B）。
    internal readonly record struct PendingEdgeOrdinal(
      int SourceOrdinal,
      int TargetOrdinal,
      NLCPGEdgeKind Kind,
      NLCPGEdgeLabel? StructuredLabel,
      NLCPGContextId? ContextId,
      NLCPGCallSiteContext? CallSiteContext);

    internal readonly record struct MutableGraphFacts(
      IReadOnlyList<NLCPGNode> Nodes,
      IReadOnlyList<PendingEdge> PendingEdges);
}
