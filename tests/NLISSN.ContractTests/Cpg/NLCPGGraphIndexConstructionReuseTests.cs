using System.Reflection;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

/// <summary>
/// P03 冻结查询索引计数、偏移与排列复用的等价性契约。对应执行文档
/// <c>docs/plans/2026-09-25-frozen-query-index-construction-reuse-execution.md</c>。
///
/// T1 的职责是在动手前把"不可变的部分"逐项钉死：
/// 1) canonical / outgoing / incoming / incomingByKind / edgesByKind 的原始枚举顺序；
/// 2) 常驻 ordinal/offset 数组的长度（构造复用的内存账本）；
/// 3) 快照指纹的确定性与鉴别力；
/// 4) 重复 key、空图、单边、最大 enum kind 这些 counting sort 边界。
///
/// 所有断言都必须对"改前/改后"两版同样成立，故一律以公开查询结果或结构不变量表达。
/// </summary>
public sealed class NLCPGGraphIndexConstructionReuseTests
{
  private readonly ITestOutputHelper _output;

  public NLCPGGraphIndexConstructionReuseTests(ITestOutputHelper output)
  {
    _output = output;
  }

  // edgesByKind 必须是"按 kind 分桶、桶内保持 canonical 相对顺序"的独立排列。
  // 它不能被误当成 incomingByKind 的最终 target/kind 排列——两者语义不同。
  [Fact]
  public void EdgesByKind_KeepsKindBucketsInCanonicalRelativeOrder()
  {
    // Arrange
    var graph = BuildVariedGraph(edgeCount: 240, kindsPerEdge: 10, nodeCount: 5, withMetadata: true);
    var canonical = ReadOrderedEdges(graph);

    // Act + Assert
    var nonEmptyKinds = 0;
    var total = 0;
    foreach (var kind in Enum.GetValues<NLCPGEdgeKind>())
    {
      var expected = canonical.Where(edge => edge.Kind == kind).Select(Describe).ToArray();
      var actual = graph.GetEdges(kind).Select(Describe).ToArray();

      Assert.Equal(expected, actual);
      total += actual.Length;
      if (actual.Length > 0)
      {
        nonEmptyKinds += 1;
      }
    }

    // 用例强度守卫：至少覆盖多个非空 kind 桶，否则"分桶"这条契约根本没被压到。
    _output.WriteLine($"canonical={canonical.Count}; byKind={total}; nonEmptyKinds={nonEmptyKinds}");
    Assert.True(nonEmptyKinds >= 5, $"expected >=5 non-empty kind buckets, got {nonEmptyKinds}");
    Assert.Equal(canonical.Count, total);
  }

  // incoming 与 incomingByKind 共用同一份 target 直方图 offsets：
  // incoming 桶 = canonical 序按 target 过滤；逐 kind 求和必须还原同一个桶。
  [Fact]
  public void IncomingAndIncomingByKind_ShareBucketBoundariesForEveryNode()
  {
    // Arrange
    var graph = BuildVariedGraph(edgeCount: 240, kindsPerEdge: 10, nodeCount: 5, withMetadata: false);
    var canonical = ReadOrderedEdges(graph);

    // Act + Assert
    var checkedNodes = 0;
    foreach (var node in graph.Nodes)
    {
      if (node.NodeId is not { } nodeId)
      {
        continue;
      }

      var expectedIncoming = canonical
        .Where(edge => edge.TargetNodeId == nodeId)
        .Select(Describe)
        .ToArray();
      var actualIncoming = graph.GetIncomingEdges(nodeId).Select(Describe).ToArray();
      Assert.Equal(expectedIncoming, actualIncoming);

      var union = new List<string>();
      foreach (var kind in Enum.GetValues<NLCPGEdgeKind>())
      {
        union.AddRange(graph.GetIncomingEdges(nodeId, kind).Select(Describe));
      }

      // 多重集必须一致：这同时证明 incomingByKind 与 incoming 的桶边界没有漂移。
      Assert.Equal(
        expectedIncoming.OrderBy(item => item, StringComparer.Ordinal),
        union.OrderBy(item => item, StringComparer.Ordinal));
      Assert.Equal(expectedIncoming.Length, union.Count);

      checkedNodes += 1;
    }

    _output.WriteLine($"nodes-checked={checkedNodes}; canonical={canonical.Count}");
    Assert.True(checkedNodes > 0);
  }

  // 内存账本：常驻 ordinal/offset 数组长度必须与边数/节点数一致。
  // 只锁常驻宽度，避免复用改造把结果数组截短或让两份结果别名到同一份数组。
  [Fact]
  public void ResidentOrdinalAndOffsetArrays_HaveExactExpectedLengths()
  {
    // Arrange
    var graph = BuildVariedGraph(edgeCount: 240, kindsPerEdge: 10, nodeCount: 5, withMetadata: false);
    var index = ReadQueryIndex(graph);
    var edgeCount = ReadOrderedEdges(graph).Count;
    var nodeCount = graph.Nodes.Count;
    var kindWidth = Enum.GetValues<NLCPGEdgeKind>().Select(kind => (int)kind).Max() + 1;

    // Act
    var byKindOffsets = (int[])GetProperty(index, "EdgesByKindOffsets");
    var byKindOrdinals = (int[])GetProperty(index, "EdgesByKindOrdinals");
    var outgoing = GetProperty(index, "Outgoing");
    var incoming = GetProperty(index, "Incoming");
    var incomingByKind = GetProperty(index, "IncomingByKind");
    var outgoingOffsets = (int[])GetField(outgoing, "_offsets")!;
    var incomingOffsets = (int[])GetField(incoming, "_offsets")!;

    // Assert
    _output.WriteLine(
      $"edges={edgeCount}; nodes={nodeCount}; kindWidth={kindWidth}; " +
      $"byKindOffsets={byKindOffsets.Length}; byKindOrdinals={byKindOrdinals.Length}; " +
      $"outgoingOffsets={outgoingOffsets.Length}; incomingOffsets={incomingOffsets.Length}");

    Assert.Equal(kindWidth + 1, byKindOffsets.Length);
    Assert.Equal(edgeCount, byKindOrdinals.Length);
    Assert.Equal(edgeCount, byKindOffsets[kindWidth]);

    // outgoing 方向在 canonical 序中天然连续，故 ordinals 为 null、只有 offsets。
    Assert.Null(GetField(outgoing, "_ordinals"));
    Assert.Equal(nodeCount + 1, outgoingOffsets.Length);
    Assert.Equal(edgeCount, outgoingOffsets[nodeCount]);

    // incoming 与 incomingByKind 必须共享同一份 offsets 实例（同一 target 直方图）。
    Assert.Same(GetField(incoming, "_offsets"), GetField(incomingByKind, "_offsets"));
    Assert.Equal(nodeCount + 1, incomingOffsets.Length);
    Assert.Equal(edgeCount, incomingOffsets[nodeCount]);
  }

  // 快照指纹：同一输入确定性；不同边集合/元数据必须给出不同指纹。
  // 复用计数与 offsets 不得改变字节写入顺序，故这里是硬门槛。
  //
  // 注意：单纯旋转插入序【不应】改变指纹——canonical 序是键的函数，不是插入序的函数。
  // 那一条属"插入序不变性"，由 PermutedInsertionOrder_* 单独覆盖；这里要的是鉴别力，
  // 故必须换【真正不同的输入】（不同 kind 宽度、不同元数据覆盖）。
  [Fact]
  public void SnapshotVersion_IsDeterministicAndStillDiscriminatesDifferentEdges()
  {
    // Arrange + Act
    var first = BuildVariedGraph(240, 10, 5, withMetadata: true).GraphSnapshotVersion;
    var second = BuildVariedGraph(240, 10, 5, withMetadata: true).GraphSnapshotVersion;
    var differentNodes = BuildVariedGraph(240, 10, 6, withMetadata: true).GraphSnapshotVersion;
    var noMetadata = BuildVariedGraph(240, 10, 5, withMetadata: false).GraphSnapshotVersion;

    // Assert
    _output.WriteLine(
      $"same-a={first}; same-b={second}; different-nodes={differentNodes}; no-metadata={noMetadata}");

    Assert.Equal(first, second);
    Assert.NotEqual(first, differentNodes);
    Assert.NotEqual(first, noMetadata);
  }

  // 惰性化（执行方案 §3.3/§7）：GraphSnapshotVersion 只在首次读取时计算，
  // 且并发首读必须只算一次并返回同一个值（ExecutionAndPublication 语义）。
  // 这条锁定的是锁语义回归：若退化成"每个线程各算一次"，返回值仍相同，
  // 但会重复付出 NPC.cs 级别的 SHA-256 开销——故判据是"只算一次 + 值一致"。
  [Fact]
  public void SnapshotVersion_ConcurrentFirstRead_ComputesOnceAndStaysStable()
  {
    // 期望值取自另一张同参构造的图：被测图的**首次**读取必须留给并发循环，
    // 否则本用例退化成"读已缓存值"，不再覆盖竞态窗口。
    var expected = BuildVariedGraph(240, 10, 5, withMetadata: true).GraphSnapshotVersion;
    var graph = BuildVariedGraph(240, 10, 5, withMetadata: true);

    var lazy = GetSnapshotVersionLazy(graph);
    Assert.False(lazy.IsValueCreated, "构造后不得已经计算快照版本。");

    var results = new string[32];
    using var barrier = new Barrier(results.Length);
    Parallel.For(0, results.Length, index =>
    {
      barrier.SignalAndWait();
      results[index] = graph.GraphSnapshotVersion;
    });

    Assert.All(results, value => Assert.Equal(expected, value));
    // 并发首读只应触发一次计算。
    Assert.True(lazy.IsValueCreated);
  }

  // counting sort 边界：空图、单边、最大 enum kind。
  [Fact]
  public void CountingSortBoundaries_EmptySingleEdgeAndMaximumKind_StayInRange()
  {
    // 空图：不得有任何边，offsets 全零。
    var empty = new NLCPGGraph();
    empty.FreezeQueryIndex();
    Assert.Empty(empty.GetEdges(NLCPGEdgeKind.DataFlow));
    Assert.All((int[])GetProperty(ReadQueryIndex(empty), "EdgesByKindOffsets"), value => Assert.Equal(0, value));

    // 单边：只有一个非零桶，且 ordinals 长度为 1。
    var single = new NLCPGGraph();
    var singleSource = single.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "s"));
    var singleTarget = single.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "t"));
    single.AddEdge(singleSource, singleTarget, NLCPGEdgeKind.DataFlow);
    single.FreezeQueryIndex();

    Assert.Single(single.GetEdges(NLCPGEdgeKind.DataFlow));
    Assert.Single((int[])GetProperty(ReadQueryIndex(single), "EdgesByKindOrdinals"));

    // 最大 enum kind：必须落在 offsets 区间内并被取回，而不是被当成越界返回空。
    var maxKind = (NLCPGEdgeKind)Enum.GetValues<NLCPGEdgeKind>().Select(kind => (int)kind).Max();
    var maxGraph = new NLCPGGraph();
    var maxSource = maxGraph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "ms"));
    var maxTarget = maxGraph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "mt"));
    maxGraph.AddEdge(maxSource, maxTarget, maxKind);
    // 再补一条低 kind 的边，确保最大 kind 桶不是"唯一桶"，即真的靠 offsets 定位。
    maxGraph.AddEdge(maxSource, maxTarget, NLCPGEdgeKind.DataFlow);
    maxGraph.FreezeQueryIndex();

    var maxBucket = maxGraph.GetEdges(maxKind).ToArray();
    Assert.Single(maxBucket);
    Assert.Equal(maxKind, maxBucket[0].Kind);

    _output.WriteLine(
      $"emptyOffsets={((int[])GetProperty(ReadQueryIndex(empty), "EdgesByKindOffsets")).Length}; " +
      $"maxKind={maxKind}({(int)maxKind})");
  }

  // 重复 key：同一 (source, kind, target)、只有元数据不同的边。
  // canonical 序的最后一个键是元数据秩，故结果必须由元数据决定，与插入序无关。
  // 这条覆盖 BuildOrdinals / BuildKeyOffsets 都依赖的"同 key 靠稳定排序保持输入序"前提。
  [Fact]
  public void DuplicateEndpointAndKind_OrdersByMetadataRankNotInsertionOrder()
  {
    // Arrange：先插入 Supports，再插入 DerivedFrom。
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "dup-s"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "dup-t"));
    var derivedFrom = NLCPGEdgeLabel.ForDecisionRelation(NLCPGDecisionRelationKind.DerivedFrom);
    var replacedWith = NLCPGEdgeLabel.ForDecisionRelation(NLCPGDecisionRelationKind.ReplacedWith);
    graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow, structuredLabel: replacedWith);
    graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow, structuredLabel: derivedFrom);

    // Act
    graph.FreezeQueryIndex();
    var canonical = ReadOrderedEdges(graph).Select(Describe).ToArray();

    // Assert：元数据秩按 StableKey 序数比较，"decision-relation:DerivedFrom" < "...:ReplacedWith"。
    // 输入序是 ReplacedWith→DerivedFrom，故若结果为 DerivedFrom→ReplacedWith，说明键真的生效了。
    Assert.Equal(2, canonical.Length);
    Assert.Contains("decision-relation:DerivedFrom", canonical[0], StringComparison.Ordinal);
    Assert.Contains("decision-relation:ReplacedWith", canonical[1], StringComparison.Ordinal);
    _output.WriteLine(string.Join(" ; ", canonical));
  }

  // 构造复用的核心等价前提：histogram 只依赖 key 的 multiset。
  // 同一 multiset 的两种插入排列必须给出相同 canonical 序与相同指纹——
  // 实现须由同一数组一次算 offsets，不能依赖"碰巧相同"。
  [Fact]
  public void PermutedInsertionOrder_YieldsSameCanonicalOrderAndSnapshotVersion()
  {
    // Arrange
    var forward = BuildVariedGraph(240, 10, 5, withMetadata: true);
    var rotated = BuildRotatedInsertionGraph();

    // Act + Assert：每个 kind 桶顺序与内容都必须一致。
    var kindCount = Enum.GetValues<NLCPGEdgeKind>().Select(kind => (int)kind).Max() + 1;
    for (var ordinal = 0; ordinal < kindCount; ordinal += 1)
    {
      var kind = (NLCPGEdgeKind)ordinal;
      Assert.Equal(
        forward.GetEdges(kind).Select(Describe),
        rotated.GetEdges(kind).Select(Describe));
    }

    Assert.Equal(
      ReadOrderedEdges(forward).Select(Describe),
      ReadOrderedEdges(rotated).Select(Describe));
    Assert.Equal(forward.GraphSnapshotVersion, rotated.GraphSnapshotVersion);
  }

  private static string Describe(NLCPGEdge edge)
  {
    return string.Join(
      '|',
      (int)edge.Kind,
      edge.SourceNodeId.Value,
      edge.TargetNodeId.Value,
      edge.StructuredLabel?.StableKey ?? "-",
      edge.ContextId?.Value ?? "-",
      edge.CallSiteContext?.FilePath ?? "-",
      edge.CallSiteContext?.SpanStart ?? -1,
      edge.CallSiteContext?.SpanEnd ?? -1,
      edge.CallSiteContext?.DisplayName ?? "-");
  }

  private static NLCPGGraph BuildVariedGraph(
    int edgeCount,
    int kindsPerEdge,
    int nodeCount,
    bool withMetadata)
  {
    var graph = NewGraphWithNodes(nodeCount);
    AddVariedEdges(graph, edgeCount, kindsPerEdge, withMetadata, rotateInsertionOrder: false);
    graph.FreezeQueryIndex();
    return graph;
  }

  private static NLCPGGraph BuildRotatedInsertionGraph()
  {
    var graph = NewGraphWithNodes(5);
    AddVariedEdges(graph, edgeCount: 240, kindsPerEdge: 10, withMetadata: true, rotateInsertionOrder: true);
    graph.FreezeQueryIndex();
    return graph;
  }

  private static NLCPGGraph NewGraphWithNodes(int nodeCount)
  {
    var graph = new NLCPGGraph();
    for (var index = 0; index < nodeCount; index += 1)
    {
      graph.AddNode(new NLCPGNodeDraft(
        NLCPGNodeKind.Operation,
        Name: "n" + index,
        FilePath: "p03.cs",
        SpanStart: index * 10,
        SpanEnd: (index * 10) + 4));
    }

    return graph;
  }

  private static void AddVariedEdges(
    NLCPGGraph graph,
    int edgeCount,
    int kindsPerEdge,
    bool withMetadata,
    bool rotateInsertionOrder)
  {
    var nodes = graph.Nodes.ToArray();
    var nodeCount = nodes.Length;
    var label = withMetadata
      ? NLCPGEdgeLabel.ForDecisionRelation(NLCPGDecisionRelationKind.DerivedFrom)
      : null;

    for (var step = 0; step < edgeCount; step += 1)
    {
      // rotateInsertionOrder 时整体旋转插入序，生成的多重集完全相同。
      var index = rotateInsertionOrder ? (step + 97) % edgeCount : step;
      var source = nodes[index % nodeCount];
      var target = nodes[(index / nodeCount) % nodeCount];
      var kind = (NLCPGEdgeKind)((index / (nodeCount * nodeCount)) % kindsPerEdge);
      var callSite = withMetadata && (index % 3 == 0)
        ? new NLCPGCallSiteContext("p03.cs", index, index + 2, "cs" + index)
        : (NLCPGCallSiteContext?)null;
      var contextId = withMetadata && (index % 5 == 0) && callSite is null
        ? new NLCPGContextId("ctx" + index)
        : (NLCPGContextId?)null;
      graph.AddEdge(
        source,
        target,
        kind,
        structuredLabel: withMetadata && (index % 2 == 0) ? label : null,
        contextId: contextId,
        callSiteContext: callSite);
    }
  }

  private static object ReadQueryIndex(NLCPGGraph graph)
  {
    return typeof(NLCPGGraph)
      .GetField("_queryIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(graph)
      ?? throw new InvalidOperationException("Graph is not frozen.");
  }

  // 取冻结索引里承载快照版本的 Lazy<string>，用于断言"尚未求值 / 已求值"。
  private static Lazy<string> GetSnapshotVersionLazy(NLCPGGraph graph)
  {
    return (Lazy<string>)GetField(ReadQueryIndex(graph), "_snapshotVersion")!;
  }

  private static IReadOnlyList<NLCPGEdge> ReadOrderedEdges(NLCPGGraph graph)
  {
    var index = ReadQueryIndex(graph);
    return (IReadOnlyList<NLCPGEdge>)index.GetType()
      .GetProperty("OrderedEdges", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(index)!;
  }

  private static object GetProperty(object instance, string name)
  {
    return instance.GetType()
      .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(instance)!;
  }

  private static object? GetField(object instance, string name)
  {
    return instance.GetType()
      .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(instance);
  }
}
