using System.Reflection;
using System.Runtime.CompilerServices;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

/// <summary>
/// 冻结节点单份持有的等价性与常驻门槛契约。对应执行文档
/// <c>docs/plans/2026-09-25-frozen-node-storage-compaction-execution.md</c>：
/// Task 1（冻结公开顺序与查询 oracle）与 Task 3（计数缓冲复用）。
///
/// 判据：
/// <list type="bullet">
/// <item>两条冻结入口（<c>FreezeQueryIndex</c> / <c>CreateFrozen</c>）之后，原始节点枚举序、
/// 全部节点查询、完整边载荷与 <c>GraphSnapshotVersion</c> 与冻结前一致。</item>
/// <item>冻结成功后不存在第二份常驻完整节点容器（{graph} 侧已无 NodeId→节点 字典）。</item>
/// <item>计数排序 scratch 在单次 Create 内复用，且交替大/小 keyWidth 时结果仍然正确。</item>
/// </list>
/// </summary>
public sealed class FrozenNodeStorageEquivalenceTests
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly ITestOutputHelper _output;

    public FrozenNodeStorageEquivalenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── 公开顺序 oracle ────────────────────────────────────────────────────────

    // 冻结不得改变 graph.Nodes 的枚举序：冻结前（_nodesByOrdinal 的首次入图序）与冻结后
    // （索引的输入序视图）必须逐项相同。这是本改动的核心公开契约。
    [Fact]
    public void FreezeQueryIndex_PreservesMutableNodeEnumerationOrder()
    {
        var graph = BuildMutableGraph(nodeCount: 64, edgeCount: 200);
        var beforeFreeze = graph.Nodes.ToArray();

        graph.FreezeQueryIndex();

        var afterFreeze = graph.Nodes.ToArray();
        _output.WriteLine($"nodes={afterFreeze.Length}; first-before={Describe(beforeFreeze[0])}; first-after={Describe(afterFreeze[0])}");
        Assert.Equal(beforeFreeze.Length, afterFreeze.Length);
        for (var index = 0; index < beforeFreeze.Length; index += 1)
        {
            Assert.Equal(beforeFreeze[index], afterFreeze[index]);
        }
    }

    // CreateFrozen 的输入可以乱序且 ID 非连续：graph.Nodes 必须复现【输入序】，而不是 canonical 序。
    [Fact]
    public void CreateFrozen_PreservesShuffledSparseInputOrder()
    {
        var nodes = BuildFrozenNodes(nodeCount: 32, sparseStride: 7);
        var shuffled = nodes.OrderBy(node => node.NodeId!.Value.Value % 5)
          .ThenByDescending(node => node.NodeId!.Value.Value)
          .ToArray();
        var edges = BuildFrozenEdges(shuffled);

        var graph = NLCPGGraph.CreateFrozen(shuffled, edges);

        var actual = graph.Nodes.ToArray();
        Assert.Equal(shuffled.Length, actual.Length);
        for (var index = 0; index < shuffled.Length; index += 1)
        {
            Assert.Equal(shuffled[index].NodeId, actual[index].NodeId);
        }

        // 同时确认输入序确实不是 canonical 序（否则本用例没有鉴别力）。
        Assert.NotEqual(
            shuffled.Select(node => node.NodeId!.Value).ToArray(),
            shuffled.Select(node => node.NodeId!.Value).OrderBy(id => id).ToArray());
    }

    // CreateFrozen 必须只枚举一次输入（原实现把节点一次性写进字典，同样只枚举一次）。
    [Fact]
    public void CreateFrozen_EnumeratesEachInputOnlyOnce()
    {
        var nodes = BuildFrozenNodes(nodeCount: 16, sparseStride: 3);
        var edges = BuildFrozenEdges(nodes);

        var nodeEnumerable = new SinglePassEnumerable<NLCPGNode>(nodes);
        var edgeEnumerable = new SinglePassEnumerable<NLCPGEdge>(edges);

        var graph = NLCPGGraph.CreateFrozen(nodeEnumerable, edgeEnumerable);

        Assert.Equal(nodes.Length, graph.Nodes.Count);
        Assert.Equal(1, nodeEnumerable.EnumerationCount);
        Assert.Equal(1, edgeEnumerable.EnumerationCount);
    }

    // ── 查询 oracle ───────────────────────────────────────────────────────────

    [Fact]
    public void FrozenQueries_ResolveEveryNodeAndRejectUnknownIds()
    {
        var graph = BuildMutableGraph(nodeCount: 40, edgeCount: 120);
        graph.FreezeQueryIndex();

        var known = 0;
        foreach (var node in graph.Nodes)
        {
            var resolved = graph.GetNode(node.NodeId!.Value);
            Assert.Equal(node, resolved);
            known += 1;
        }

        Assert.Equal(graph.Nodes.Count, known);

        // 未知 ID 返回 null（不是抛异常）。取一个必然未使用的 ID。
        var maxId = graph.Nodes.Max(node => node.NodeId!.Value.Value);
        Assert.Null(graph.GetNode(new NodeId(maxId + 1000)));
    }

    // 冻结前图中不存在 NodeId→节点 容器，GetNode 恒返回 null。这条是既有行为，
    // 单列护栏以防"把冻结前也接上索引"这类顺带改动。
    [Fact]
    public void GetNode_BeforeFreeze_ReturnsNullForEveryId()
    {
        var graph = BuildMutableGraph(nodeCount: 8, edgeCount: 12);

        Assert.Null(graph.GetNode(new NodeId(1)));
        Assert.Null(graph.GetNode(new NodeId(0)));

        graph.FreezeQueryIndex();
        Assert.NotNull(graph.GetNode(new NodeId(1)));
    }

    // 依赖"已知端点都必须在字典里"的方法：迁移后必须保持同样的返回值与顺序。
    [Fact]
    public void SymbolAndCallSiteQueries_MatchEdgeDerivedExpectation()
    {
        var graph = BuildMutableGraph(nodeCount: 48, edgeCount: 240);
        graph.FreezeQueryIndex();

        var symbolChecks = 0;
        var callSiteChecks = 0;
        foreach (var node in graph.Nodes)
        {
            var nodeId = node.NodeId!.Value;

            var expectedReferences = graph.GetIncomingEdges(nodeId, NLCPGEdgeKind.Ref)
              .Select(edge => graph.GetNode(edge.SourceNodeId)!.Value)
              .OrderBy(candidate => candidate.NodeId)
              .ToArray();
            Assert.Equal(expectedReferences, graph.GetSymbolReferences(nodeId));
            symbolChecks += 1;

            var expectedCallSites = graph.GetOutgoingEdges(nodeId, NLCPGEdgeKind.ContainsSymbol)
              .Select(edge => graph.GetNode(edge.TargetNodeId)!.Value)
              .Where(candidate => candidate.Kind == NLCPGNodeKind.CallSite)
              .OrderBy(candidate => candidate.NodeId)
              .ToArray();
            Assert.Equal(expectedCallSites, graph.GetMethodOwnedCallSites(nodeId));
            callSiteChecks += 1;
        }

        _output.WriteLine($"symbol-checks={symbolChecks}; call-site-checks={callSiteChecks}");
        Assert.True(symbolChecks > 0 && callSiteChecks > 0);
    }

    // 跨度查询与局部视图都必须走同一份节点载荷。
    [Fact]
    public void SpanAndLocalViewQueries_ReturnNodesFromTheSharedPayload()
    {
        var graph = BuildMutableGraph(nodeCount: 32, edgeCount: 96);
        graph.FreezeQueryIndex();

        var inSpan = graph.GetNodesInFileSpan("fns.cs", 0, int.MaxValue);
        Assert.Equal(graph.Nodes.Count(node => graph.ResolveFilePath(node) == "fns.cs"), inSpan.Count);
        Assert.All(inSpan, node => Assert.Equal(node, graph.GetNode(node.NodeId!.Value)));

        var anchor = graph.Nodes.First(node => graph.GetOutgoingEdges(node.NodeId!.Value).Count > 0);
        var view = graph.ExtractLocalView(anchor.NodeId!.Value, hops: 2);
        Assert.Equal(anchor, view.Anchor);
        Assert.All(view.Nodes, node => Assert.Equal(node, graph.GetNode(node.NodeId!.Value)));

        Assert.Throws<ArgumentException>(() => graph.ExtractLocalView(new NodeId(999_999), hops: 1));
    }

    // ── 单份常驻载荷门槛 ──────────────────────────────────────────────────────

    // 冻结成功后：(a) NLCPGGraph 侧不存在任何"完整节点容器"字段；
    // (b) 索引的 canonical 数组与输入序视图共用同一个 NLCPGNode[] 实例；
    // (c) 构图期的节点 List 已清空并归还容量。
    // 原先这里断言失败，因为 graph 持有一份常驻的 Dictionary<NodeId, NLCPGNode>，
    // 其 entry 按值内联 NLCPGNode（实测 104 B）——那正是本次要消除的第二份完整载荷。
    [Fact]
    public void AfterFreeze_GraphHoldsNoSecondCompleteNodeContainer()    {
        var graph = BuildMutableGraph(nodeCount: 96, edgeCount: 300);
        graph.FreezeQueryIndex();

        Assert.Null(
            typeof(NLCPGGraph).GetField("_nodesByNodeId", InstanceNonPublic));

        var nodesByOrdinal = (System.Collections.ICollection)typeof(NLCPGGraph)
          .GetField("_nodesByOrdinal", InstanceNonPublic)!
          .GetValue(graph)!;
        Assert.Equal(0, nodesByOrdinal.Count);

        var index = ReadQueryIndex(graph);
        var canonical = (NLCPGNode[])ReadProperty(index, "OrderedNodes");
        var canonicalAlias = (NLCPGNode[])ReadProperty(index, "CanonicalNodes");
        Assert.Same(canonical, canonicalAlias);

        // graph.Nodes 的载体必须是 OrdinalNodeList，且其节点数组就是同一个 canonical 数组。
        var inputOrdered = ReadProperty(index, "InputOrderedNodes");
        var viewNodes = (NLCPGNode[])inputOrdered.GetType()
          .GetField("_nodes", InstanceNonPublic)!
          .GetValue(inputOrdered)!;
        Assert.Same(canonical, viewNodes);

        _output.WriteLine(
          $"nodes={graph.Nodes.Count}; canonical={canonical.Length}; " +
          $"input-order={((int[])inputOrdered.GetType()
            .GetField("_ordinals", InstanceNonPublic)!
            .GetValue(inputOrdered)!).Length}");
    }

    // 索引必须【接管】调用方传入的节点数组实例，而不是再复制一份。
    //
    // 判别力：NLCPGGraphIndex.Create 早先无条件写 `nodes.ToArray()`。由于两个生产调用方
    // （CreateFrozen / FreezeQueryIndex）传进来的都是它们刚刚独占新建、且此后不再复用的
    // 数组，那次复制既在冻结峰值上多出一份完整载荷（nodeCount × 104 B），又让调用方那份
    // 立刻变成垃圾。断言"索引持有的数组与传入的是同一实例"是唯一能区分这两版的行为，
    // 任何顺序/查询等价性测试都看不出来。
    //
    // 这里直接调 internal 的 Create（ContractTests 是 NLCPG 的友元程序集），
    // 因为经由 CreateFrozen 时中间还会多一层它自己的 ToArray，测不到本方法的接管行为。
    [Fact]
    public void Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace()
    {
        // 故意用 NodeId 降序的输入：若实现改为"复制后排序副本"，调用方数组会保持降序，
        // 于是下面的升序断言同样会失败——排序断言因此是有判别力的，不是恒真。
        var source = BuildDescendingFrozenNodes(nodeCount: 512);

        var index = NLCPGGraphIndex.Create(source, Array.Empty<NLCPGEdge>());
        var canonical = (NLCPGNode[])ReadProperty(index, "OrderedNodes");

        // 同一实例 ⇒ 没有发生第二次完整载荷复制。
        Assert.Same(source, canonical);
        Assert.Equal(source.Length, canonical.Length);
        // 就地排序 ⇒ 传入的那个数组实例本身现在必须是 NodeId 升序。
        Assert.True(
          canonical.Zip(canonical.Skip(1)).All(pair =>
            pair.First.NodeId!.Value.CompareTo(pair.Second.NodeId!.Value) < 0),
          "canonical 数组未就地按 NodeId 升序排序。");
    }

    // 非数组输入没有"调用方数组可接管"，必须复制一份再排序，
    // 否则就地排序会改写调用方传入的集合。这条锁住 `?? nodes.ToArray()` 的回退分支。
    [Fact]
    public void Create_CopiesANonArrayInputSoTheCallersCollectionIsNotReordered()
    {
        var source = BuildDescendingFrozenNodes(nodeCount: 64);
        var list = new List<NLCPGNode>(source);

        var index = NLCPGGraphIndex.Create(list, Array.Empty<NLCPGEdge>());
        var canonical = (NLCPGNode[])ReadProperty(index, "OrderedNodes");

        Assert.NotSame(source, canonical);
        // 索引自己拿到了升序副本……
        Assert.True(
          canonical.Zip(canonical.Skip(1)).All(pair =>
            pair.First.NodeId!.Value.CompareTo(pair.Second.NodeId!.Value) < 0),
          "非数组输入也应得到按 NodeId 升序的索引数组。");
        // ……而调用方传入的 List 必须原封不动（仍是原来的降序）。
        Assert.Equal(source.Select(node => node.NodeId), list.Select(node => node.NodeId));
    }

    // 冻结路径上的运行时证据（计划 §7 第 3 条要求"独立记录冻结峰值"，不得以"常驻变小"替代）。
    //
    // 这里量的不是稳态常驻，而是【一次 Create 实际分配了多少字节】：若实现又复制一份完整
    // 节点载荷，分配额必然 ≥ 一份载荷（nodeWidth × nodeCount）；接管路径则只分配
    // int[] 临时量与索引自身结构（NodeId→ordinal 字典 + 输入序 + 临时序数），远小于一份载荷。
    //
    // 该断言可判别：把 Create 的 `as NLCPGNode[] ?? nodes.ToArray()` 改回无条件
    // `nodes.ToArray()` 后，分配额会跨过一份载荷这条线而失败。
    [Fact]
    public void Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath()
    {
        const int NodeCount = 8192;
        var nodeWidth = Unsafe.SizeOf<NLCPGNode>();
        var payloadBytes = (long)NodeCount * nodeWidth;

        // 预热：让 JIT 与静态初始化先付掉，避免把一次性成本算进本次测量。
        _ = NLCPGGraphIndex.Create(BuildFrozenNodes(nodeCount: 16, sparseStride: 1), Array.Empty<NLCPGEdge>());

        var source = BuildFrozenNodes(nodeCount: NodeCount, sparseStride: 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var index = NLCPGGraphIndex.Create(source, Array.Empty<NLCPGEdge>());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        _output.WriteLine(
          $"nodes={NodeCount}; node-width={nodeWidth}; one-payload={payloadBytes} B; " +
          $"allocated-by-one-Create={allocated} B; allocated/one-payload={allocated / (double)payloadBytes:P1}");

        // 先断分配额（它给出可读的字节数），再断同一实例（更精确的因果）。
        // 顺序有意如此：变异回无条件 ToArray 时，这里先报出"超出一份载荷"的具体字节数，
        // 而不是只报一句 Assert.Same 的实例不等——两者的失败信息都指向同一处回归。
        Assert.True(
          allocated < payloadBytes,
          $"一次 Create 分配了 {allocated} B，已超过一份完整节点载荷 {payloadBytes} B" +
          $"（{NodeCount} × {nodeWidth}）——说明冻结路径上又复制了一份完整节点载荷。");

        // 接管语义：索引持有的就是调用方那个数组实例。
        Assert.Same(source, (NLCPGNode[])ReadProperty(index, "OrderedNodes"));
    }

    // 常驻节点存储账本：只数数组/容器本体（载荷与受控容器存活字节），
    // 不含每个对象 24 B 的对象头与 GC 记账——那些在两版之间相同，会在比值里稀释真实差异。
    //
    // PRE 基线不是纸上推导：这里把被删除的那份容器按原类型、原宽度真实物化一遍再量（Dictionary<NodeId, NLCPGNode>，entry 按值内联 NLCPGNode），故"下降比例"是实测比值。
    [Fact]
    public void NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload()
    {
        const int NodeCount = 8192;
        var graph = BuildMutableGraph(nodeCount: NodeCount, edgeCount: 4096);
        graph.FreezeQueryIndex();

        var index = ReadQueryIndex(graph);
        var canonical = (NLCPGNode[])ReadProperty(index, "OrderedNodes");
        var nodeOrdinals = (System.Collections.IDictionary)ReadProperty(index, "NodeOrdinals");
        var insertionOrder = (int[])ReadProperty(index, "NodeInsertionOrder");

        var nodeWidth = Unsafe.SizeOf<NLCPGNode>();
        var payloadBytes = (long)canonical.Length * nodeWidth;
        var ordinalsBytes = CountDictionaryBytes(nodeOrdinals, sizeof(int));
        var insertionBytes = (long)insertionOrder.Length * sizeof(int);

        // 只能有一份完整节点载荷数组：任何第二份都会让下面这条断言失效。
        // 两个根共用同一份 visited/payload 集合，故共享的 canonical 数组只计一次。
        var payloadArrays = CountNodePayloadArrays(
          (typeof(NLCPGGraph), graph),
          (index.GetType(), index));
        Assert.Equal(1, payloadArrays);

        var totalBytes = payloadBytes + ordinalsBytes + insertionBytes;

        // ── PRE 基线：按原实现真实重建那份被删除的常驻节点字典 ──────────────────
        // 原 NLCPGGraph 持有一个填满全部节点的 Dictionary<NodeId, NLCPGNode>，
        // 其 Entry 按值内联 NLCPGNode（12 B 头 + 实测 104 B 值），此处按同一类型、
        // 同一填充方式物化后逐数组测量，而不是套用估算公式。
        var legacyNodesByNodeId = new Dictionary<NodeId, NLCPGNode>(canonical.Length);
        foreach (var node in canonical)
        {
            legacyNodesByNodeId[node.NodeId!.Value] = node;
        }

        var legacyDictionaryBytes = CountDictionaryBytes(legacyNodesByNodeId, nodeWidth);
        var legacyTotalBytes = payloadBytes + ordinalsBytes + legacyDictionaryBytes;
        // 对齐用例强度：重建的字典必须真的持有全部节点，否则基线会被低估。
        Assert.Equal(canonical.Length, legacyNodesByNodeId.Count);

        var reduction = 1.0 - ((double)totalBytes / legacyTotalBytes);
        _output.WriteLine(
          $"nodes={canonical.Length}; node-width={nodeWidth}; node-payload-arrays={payloadArrays}; " +
          $"payload-bytes={payloadBytes}; node-ordinals-bytes={ordinalsBytes}; " +
          $"insertion-order-bytes={insertionBytes}; total-bytes={totalBytes}; " +
          $"legacy-node-dictionary-bytes={legacyDictionaryBytes}; legacy-total-bytes={legacyTotalBytes}; " +
          $"reduction={reduction:P2}");

        // 输入序排列的宽度必须是 4 B/节点——它是本次唯一的常驻新增。
        Assert.Equal(canonical.Length, insertionOrder.Length);
        Assert.Equal((long)canonical.Length * sizeof(int), insertionBytes);

        // 采纳门槛：节点存储子系统的保留字节下降至少 30%（数组头、输入序、NodeOrdinals
        // 与共享引用去重后统一计数）。
        Assert.True(
          reduction >= 0.30,
          $"节点存储子系统保留字节只下降 {reduction:P2}（{totalBytes} vs {legacyTotalBytes}），未达 30% 门槛。");
    }

    // ── 计数缓冲复用（Task 3） ─────────────────────────────────────────────────

    // CountingSortPass 的私有签名必须保留"由调用方提供 scratch"这一形态，
    // 否则每趟各分配 int[keyWidth + 1] 的旧行为可能被无声地改回来。
    [Fact]
    public void CountingSortPass_TakesCallerProvidedScratch()
    {
        var method = FindCountingSortPass();
        var parameters = method.GetParameters();

        Assert.Equal(5, parameters.Length);
        Assert.Equal(typeof(int[]), parameters[0].ParameterType);
        Assert.Equal(typeof(int[]), parameters[1].ParameterType);
        Assert.Equal(typeof(int[]), parameters[2].ParameterType);
        Assert.Equal(typeof(int), parameters[3].ParameterType);
        Assert.True(parameters[4].ParameterType.IsClass, "第 5 个形参应是 scratch 缓冲对象。");
        Assert.Equal(
          typeof(int[]),
          parameters[4].ParameterType.GetField("_positions", InstanceNonPublic)!.FieldType);
    }

    // 交替大/小 keyWidth（含空输入与全同键）必须在同一份 scratch 上给出正确结果。
    // 这里直接驱动私有 CountingSortPass，因为"大宽度之后紧跟小宽度"正是脏尾部 bug 的触发条件，
    // 而公开路径上的宽度组合不可控。同时顺带验证每次调用不再分配 keyWidth 级缓冲。
    [Fact]
    public void CountingSortPass_ReusesScratchAcrossAlternatingKeyWidths()
    {
        var method = FindCountingSortPass();
        var scratch = Activator.CreateInstance(method.GetParameters()[4].ParameterType, nonPublic: true)!;

        // ① 大宽度：4096 个桶。
        var largeKeys = BuildCountingKeys(count: 4096, keyWidth: 4096, keySelector: index => index);
        AssertCountingSortMatchesOracle(method, scratch, largeKeys, keyWidth: 4096);

        // ② 紧随其后的小宽度：若 scratch 未清活动区间，上一步的脏尾部会污染桶边界。
        var smallKeys = BuildCountingKeys(count: 512, keyWidth: 4, keySelector: index => index % 4);
        AssertCountingSortMatchesOracle(method, scratch, smallKeys, keyWidth: 4);

        // ③ 再回到大宽度（交替），并覆盖大量相同键（稳定性）。
        var tieKeys = BuildCountingKeys(count: 4096, keyWidth: 64, keySelector: index => 7);
        AssertCountingSortMatchesOracle(method, scratch, tieKeys, keyWidth: 64);

        // ④ 空输入。
        AssertCountingSortMatchesOracle(method, scratch, Array.Empty<int>(), keyWidth: 4096);

        // ⑤ 单元素、keyWidth = 1。
        AssertCountingSortMatchesOracle(method, scratch, new[] { 0 }, keyWidth: 1);

        // ⑥ 分配行为：scratch 已扩容到 4096 后，反复调用不得再分配 keyWidth 级缓冲。
        var destination = new int[4096];
        var buffer = new int[4096];
        var args = new object[] { tieKeys, destination, tieKeys, 4096, scratch };
        for (var iteration = 0; iteration < 3; iteration += 1)
        {
            method.Invoke(null, args);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        const int Iterations = 16;
        for (var iteration = 0; iteration < Iterations; iteration += 1)
        {
            method.Invoke(null, args);
        }

        var allocatedPerCall = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / Iterations;
        _output.WriteLine($"allocated-per-call={allocatedPerCall} B; legacy-per-call={4 * (4096 + 1)} B");

        // 旧行为每趟分配 4 * (keyWidth + 1) = 16,388 B；这里给反射调用本身留足余量。
        Assert.True(
          allocatedPerCall < 4096,
          $"CountingSortPass 每次调用分配 {allocatedPerCall} B，仍未复用调用方提供的 scratch。");
    }

    // 公开路径上的稳定性：完全并列的重复边由构图期去重，冻结后只剩一条且端点不变。
    // （"并列键的相对次序"由 CountingSortPass_ReusesScratchAcrossAlternatingKeyWidths 的
    //   全同键用例直接驱动，公开路径无法造出既并列又不被去重的两条边。）
    [Fact]
    public void Freeze_WithFullyTiedDuplicateEdges_DeduplicatesToSingleEdge()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "s"));
        var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "t"));
        for (var index = 0; index < 64; index += 1)
        {
            // 同一 (source, target, kind, metadata) ⇒ 全部排序键并列 ⇒ 构图期去重为一条。
            graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow);
        }

        graph.FreezeQueryIndex();

        // 冻结后才分配 NodeId，故端点必须从冻结后的节点上读。
        var frozenSource = graph.Nodes.Single(node => graph.ResolveName(node) == "s");
        var frozenTarget = graph.Nodes.Single(node => graph.ResolveName(node) == "t");
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(frozenSource.NodeId!.Value, edge.SourceNodeId);
        Assert.Equal(frozenTarget.NodeId!.Value, edge.TargetNodeId);
    }

    // 空图/单节点/稀疏 ID 的边界：冻结不得抛错，且不得留下第二份载荷。
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Freeze_WithDegenerateNodeCounts_StaysConsistent(int nodeCount)
    {
        var graph = new NLCPGGraph();
        for (var index = 0; index < nodeCount; index += 1)
        {
            graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Operation,
              Name: "n" + index,
              FilePath: "degenerate.cs",
              SpanStart: index,
              SpanEnd: index + 1));
        }

        graph.FreezeQueryIndex();

        Assert.Equal(nodeCount, graph.Nodes.Count);
        Assert.Empty(graph.Edges);
        Assert.Equal(64, graph.GraphSnapshotVersion.Length);
        Assert.Null(typeof(NLCPGGraph).GetField("_nodesByNodeId", InstanceNonPublic));
    }

    // ── 冻结入口的原错误边界 ──────────────────────────────────────────────────

    [Fact]
    public void CreateFrozen_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
          () => NLCPGGraph.CreateFrozen(null!, Array.Empty<NLCPGEdge>()));
        Assert.Throws<ArgumentNullException>(
          () => NLCPGGraph.CreateFrozen(Array.Empty<NLCPGNode>(), null!));
    }

    [Fact]
    public void CreateFrozen_RejectsNodesWithoutNodeId()
    {
        var node = new NLCPGNode(NLCPGNodeKind.Operation, NameId: 1);
        var failure = Assert.Throws<ArgumentException>(
          () => NLCPGGraph.CreateFrozen(new[] { node }, Array.Empty<NLCPGEdge>()));
        Assert.Equal("nodes", failure.ParamName);
    }

    [Fact]
    public void CreateFrozen_RejectsDuplicateNodeIds()
    {
        var first = new NLCPGNode(NLCPGNodeKind.Operation, NameId: 1, NodeId: new NodeId(7));
        var second = new NLCPGNode(NLCPGNodeKind.Operation, NameId: 2, NodeId: new NodeId(7));

        // 与原 _nodesByNodeId.Add 完全一致：ArgumentException，且不带 ParamName
        // （框架的字典重复键异常不带参数名，这里刻意保持原样，不做"顺手改进"）。
        var failure = Assert.Throws<ArgumentException>(
          () => NLCPGGraph.CreateFrozen(new[] { first, second }, Array.Empty<NLCPGEdge>()));
        Assert.Null(failure.ParamName);
        Assert.Contains("7", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFrozen_RejectsEdgesWithUnknownEndpoints()
    {
        var node = new NLCPGNode(NLCPGNodeKind.Operation, NameId: 1, NodeId: new NodeId(1));
        var edge = new NLCPGEdge(new NodeId(1), new NodeId(2), NLCPGEdgeKind.DataFlow);

        var failure = Assert.Throws<ArgumentException>(
          () => NLCPGGraph.CreateFrozen(new[] { node }, new[] { edge }));
        Assert.Equal("edges", failure.ParamName);
    }

    // ── 夹具与反射工具 ────────────────────────────────────────────────────────

    private static NLCPGGraph BuildMutableGraph(int nodeCount, int edgeCount)
    {
        var graph = new NLCPGGraph();
        var nodes = new NLCPGNode[nodeCount];
        for (var index = 0; index < nodeCount; index += 1)
        {
            var kind = (index % 4) switch
            {
                0 => NLCPGNodeKind.Operation,
                1 => NLCPGNodeKind.CallSite,
                2 => NLCPGNodeKind.Method,
                _ => NLCPGNodeKind.SymbolType,
            };
            nodes[index] = graph.AddNode(new NLCPGNodeDraft(
              kind,
              Name: "n" + index,
              FullName: "fns.n" + index,
              FilePath: "fns.cs",
              SpanStart: index * 8,
              SpanEnd: (index * 8) + 4));
        }

        var kinds = new[]
        {
          NLCPGEdgeKind.DataFlow,
          NLCPGEdgeKind.Ref,
          NLCPGEdgeKind.ContainsSymbol,
          NLCPGEdgeKind.CfgNext,
        };
        for (var index = 0; index < edgeCount; index += 1)
        {
            var source = nodes[index % nodeCount];
            var target = nodes[(index * 7 + 3) % nodeCount];
            graph.AddEdge(
              source,
              target,
              kinds[index % kinds.Length],
              structuredLabel: index % 5 == 0
                ? NLCPGEdgeLabel.ForDecisionRelation(NLCPGDecisionRelationKind.DerivedFrom)
                : null,
              contextId: index % 11 == 0 ? new NLCPGContextId("ctx" + (index % 17)) : null);
        }

        return graph;
    }

    // NodeId 严格降序的节点数组：用于证明 Create 对数组输入是"就地排序同一实例"，
    // 而不是"复制后排序副本"（后者会让传入数组保持降序）。
    private static NLCPGNode[] BuildDescendingFrozenNodes(int nodeCount)
    {
        var nodes = new NLCPGNode[nodeCount];
        for (var index = 0; index < nodeCount; index += 1)
        {
            var nodeId = (uint)(nodeCount - index);
            nodes[index] = new NLCPGNode(
              NLCPGNodeKind.Operation,
              NameId: nodeId,
              FilePathId: 1,
              SpanStart: index,
              SpanEnd: index + 2,
              NodeId: new NodeId(nodeId),
              StableAnchor: new StableNodeAnchor(
                NLCPGNodeKind.Operation,
                FilePathId: 1,
                SpanStart: index,
                SpanEnd: index + 2,
                StableNodeRole.Operation,
                Ordinal: 0,
                ExtraKeyId: nodeId));
        }

        return nodes;
    }

    private static NLCPGNode[] BuildFrozenNodes(int nodeCount, int sparseStride)
    {
        var nodes = new NLCPGNode[nodeCount];
        for (var index = 0; index < nodeCount; index += 1)
        {
            nodes[index] = new NLCPGNode(
              NLCPGNodeKind.Operation,
              NameId: (uint)(index + 1),
              FilePathId: 1,
              SpanStart: index,
              SpanEnd: index + 2,
              NodeId: new NodeId((uint)((index * sparseStride) + 3)),
              StableAnchor: new StableNodeAnchor(
                NLCPGNodeKind.Operation,
                FilePathId: 1,
                SpanStart: index,
                SpanEnd: index + 2,
                StableNodeRole.Operation,
                Ordinal: 0,
                ExtraKeyId: (uint)(index + 1)));
        }

        return nodes;
    }

    private static NLCPGEdge[] BuildFrozenEdges(IReadOnlyList<NLCPGNode> nodes)
    {
        var edges = new NLCPGEdge[nodes.Count];
        for (var index = 0; index < nodes.Count; index += 1)
        {
            edges[index] = new NLCPGEdge(
              nodes[index].NodeId!.Value,
              nodes[(index + 1) % nodes.Count].NodeId!.Value,
              NLCPGEdgeKind.DataFlow);
        }

        return edges;
    }

    private static int[] BuildCountingKeys(int count, int keyWidth, Func<int, int> keySelector)
    {
        var keys = new int[count];
        for (var index = 0; index < count; index += 1)
        {
            keys[index] = keySelector(index) % keyWidth;
        }

        return keys;
    }

    private static void AssertCountingSortMatchesOracle(
      MethodInfo method,
      object scratch,
      int[] keys,
      int keyWidth)
    {
        var source = new int[keys.Length];
        for (var index = 0; index < source.Length; index += 1)
        {
            source[index] = index;
        }

        var expected = Enumerable.Range(0, keys.Length)
          .OrderBy(index => keys[index])
          .ToArray();
        var destination = new int[keys.Length];
        method.Invoke(null, new object[] { source, destination, keys, keyWidth, scratch });

        Assert.Equal(expected, destination);
    }

    private static MethodInfo FindCountingSortPass()
    {
        // 显式按形参个数取 5 参重载：另有 (…, int[] offsetsOut) 的 6 参重载，
        // 用 GetMethod(name) 会因重载歧义抛 AmbiguousMatchException。
        return typeof(NLCPGGraphIndex)
          .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
          .SingleOrDefault(method =>
            method.Name == "CountingSortPass" && method.GetParameters().Length == 5)
          ?? throw new InvalidOperationException(
            "NLCPGGraphIndex.CountingSortPass(int[], int[], int[], int, <scratch>) 未找到；" +
            "计数缓冲复用补丁可能已被整体回退。");
    }

    private static string Describe(NLCPGNode node)
    {
        return $"{node.NodeId}:{node.Kind}:{node.NameId}:{node.SpanStart}";
    }

    private static object ReadProperty(object instance, string name)
    {
        return instance.GetType().GetProperty(name, InstanceNonPublic)!.GetValue(instance)!;
    }

    private static object ReadQueryIndex(NLCPGGraph graph)
    {
        return typeof(NLCPGGraph).GetField("_queryIndex", InstanceNonPublic)!.GetValue(graph)
          ?? throw new InvalidOperationException("Graph is not frozen.");
    }

    // 统计字典的受控容器字节：桶数组 + entry 数组。
    // entry = hashCode 4 + next 4 + key 4 + value valueWidth，按 4 B 对齐。
    // 这是"受控容器存活字节"口径，不含对象头与 GC 记账（两版相同，会稀释比值）。
    private static long CountDictionaryBytes(System.Collections.IDictionary dictionary, int valueWidth)
    {
        var type = dictionary.GetType();
        var buckets = (Array)type.GetField("_buckets", InstanceNonPublic)!.GetValue(dictionary)!;
        var entries = (Array)type.GetField("_entries", InstanceNonPublic)!.GetValue(dictionary)!;
        var entryWidth = 12 + valueWidth;
        return ((long)buckets.Length * sizeof(int)) + ((long)entries.Length * entryWidth);
    }

    // 递归数出某个对象图里"完整节点载荷数组"（非空的 NLCPGNode[]）。
    // 多个根共用同一份已访问集合与结果集合，故同一数组实例（含跨根共享的 canonical 数组）
    // 只计一次——这正是"单份载荷"要数的口径。
    private static int CountNodePayloadArrays(params (Type Type, object Instance)[] roots)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var payloads = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var (_, instance) in roots)
        {
            Walk(instance, visited, payloads, depth: 0);
        }

        return payloads.Count;
    }

    private static void Walk(
      object? instance,
      HashSet<object> visited,
      HashSet<object> payloads,
      int depth)
    {
        if (instance is null || depth > 4 || !visited.Add(instance))
        {
            return;
        }

        if (instance is NLCPGNode[] nodes)
        {
            if (nodes.Length > 0)
            {
                payloads.Add(nodes);
            }

            return;
        }

        // 只下钻 NLCPG 程序集与 xunit 测试程序集里的类型，避免走进 Roslyn / BCL 的大对象图。
        if (instance.GetType().Assembly != typeof(NLCPGGraph).Assembly &&
            instance.GetType().Assembly != typeof(FrozenNodeStorageEquivalenceTests).Assembly)
        {
            return;
        }

        // 数组只沿元素走；只读集合/字典直接走其内部数组字段。
        if (instance is Array array)
        {
            if (array.Rank != 1)
            {
                return;
            }

            foreach (var item in array)
            {
                if (item is not null)
                {
                    Walk(item, visited, payloads, depth + 1);
                }
            }

            return;
        }

        foreach (var field in instance.GetType().GetFields(InstanceNonPublic))
        {
            var fieldType = field.FieldType;
            if (fieldType.IsPrimitive || fieldType == typeof(string) || fieldType.IsEnum)
            {
                continue;
            }

            Walk(field.GetValue(instance), visited, payloads, depth + 1);
        }
    }

    // 只允许枚举一次（第二次枚举直接抛错）的输入序列，用来证明冻结入口只枚举一次。
    private sealed class SinglePassEnumerable<T> : IEnumerable<T>
    {
        private readonly IReadOnlyList<T> _items;

        internal SinglePassEnumerable(IReadOnlyList<T> items)
        {
            _items = items;
        }

        internal int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount += 1;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The input sequence was enumerated more than once.");
            }

            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
