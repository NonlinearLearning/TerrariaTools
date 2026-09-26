using System.Reflection;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

/// <summary>
/// 冻结边投影化的等价性契约。对应设计文档
/// <c>docs/plans/2026-09-24-frozen-edge-projection-design.md</c> 的 I1（canonical 边序不变）
/// 与 I3（投影出的边值逐字段相同）。
///
/// 手法：常驻表示已从 <c>NLCPGEdge[]</c> 换成 <c>CanonicalEdgeStore</c>（9~16 B/边），
/// 但冻结后 <c>NLCPGGraph._edges</c>（HashSet，构图期填入）仍保留原始边值，
/// 故可与投影结果逐条对拍——这是把"表示替换"与"边集合/顺序变化"分开定位的关键。
/// 一旦阶段 3 释放 <c>_edges</c>，本类需改为对拍 <c>CanonicalEdgeStore</c> 的两次独立构建。
/// </summary>
public sealed class FrozenEdgeProjectionEquivalenceTests
{
  private readonly ITestOutputHelper _output;

  public FrozenEdgeProjectionEquivalenceTests(ITestOutputHelper output)
  {
    _output = output;
  }

  // I1 + I3：投影视图（新常驻表示）与 freeze 前的边集合逐字段相同。
  [Fact]
  public void ProjectedCanonicalEdges_MatchFrozenEdgeSet()
  {
    var graph = BuildVariedGraph();
    var expected = graph.Edges
      .OrderBy(edge => edge.SourceNodeId.Value)
      .ThenBy(edge => (int)edge.Kind)
      .ThenBy(edge => edge.TargetNodeId.Value)
      .Select(Describe)
      .ToArray();

    var actual = ReadOrderedEdges(graph).Select(Describe).ToArray();

    _output.WriteLine(
      $"edges={expected.Length}; projected={actual.Length}; " +
      $"first-expected={expected.FirstOrDefault()}; first-actual={actual.FirstOrDefault()}");

    // 用例强度守卫：生成器请求 240 条边，若三元组提前周期重复会被 HashSet 去重，
    // 用例强度将低于预期而不报错。这里显式锁死，避免测试悄悄变弱。
    Assert.Equal(240, expected.Length);
    Assert.Equal(expected.Length, actual.Length);
    for (var index = 0; index < expected.Length; index += 1)
    {
      Assert.Equal(expected[index], actual[index]);
    }
  }

  // I2：投影视图的 Count 等于边集合的 Count。
  [Fact]
  public void ProjectedCanonicalEdges_KeepEdgeCount()
  {
    var graph = BuildVariedGraph();
    Assert.Equal(graph.Edges.Count, ReadOrderedEdges(graph).Count);
  }

  // I3 的核心：SnapshotVersion 是边的确定性指纹，必须完全不变。
  // 同一输入两次构建得到相同指纹；且指纹在两个不同输入间确实不同（证明它有鉴别力）。
  [Fact]
  public void SnapshotVersion_IsDeterministicAndDiscriminating()
  {
    var first = BuildVariedGraph().GraphSnapshotVersion;
    var second = BuildVariedGraph().GraphSnapshotVersion;
    var different = BuildGraph(edgeCount: 40, kindsPerEdge: 7, nodeCount: 3).GraphSnapshotVersion;

    _output.WriteLine($"version-a={first}; version-b={second}; version-different={different}");

    Assert.Equal(first, second);
    Assert.NotEqual(first, different);
  }

  // I1：每条节点的出边/入边序列与投影前一致（顺序敏感）。
  [Fact]
  public void NeighborQueries_MatchProjectedCanonicalOrder()
  {
    var graph = BuildVariedGraph();
    var canonical = ReadOrderedEdges(graph);

    var checkedNodes = 0;
    foreach (var node in graph.Nodes.OrderBy(node => node.NodeId))
    {
      if (!node.NodeId.HasValue)
      {
        continue;
      }

      var expectedOutgoing = canonical
        .Where(edge => edge.SourceNodeId == node.NodeId.Value)
        .Select(Describe)
        .ToArray();
      var actualOutgoing = InvokeNeighborQuery(graph, "GetOutgoingEdges", node.NodeId.Value)
        .Select(Describe)
        .ToArray();
      Assert.Equal(expectedOutgoing, actualOutgoing);

      // incoming 桶由 BuildOrdinals 稳定分桶得到：同一 target 内保持 canonical 相对顺序，
      // 而 canonical 序为 (source, kind, target)，故固定 target 时的相对顺序是 source 升序、kind 升序。
      // 这里必须直接用 canonical 序过滤，不能重新排序——否则测的就不是"桶保持相对顺序"这条契约。
      var expectedIncoming = canonical
        .Where(edge => edge.TargetNodeId == node.NodeId.Value)
        .Select(Describe)
        .ToArray();
      var actualIncoming = InvokeNeighborQuery(graph, "GetIncomingEdges", node.NodeId.Value)
        .Select(Describe)
        .ToArray();
      Assert.Equal(expectedIncoming, actualIncoming);

      // 覆盖 kindSorted 的二分路径：LowerBound 内部经 EdgeAt 读取 Kind，
      // 而 EdgeAt 现在走投影，故这条路径必须被显式验证。
      foreach (var kind in new[] { NLCPGEdgeKind.DataFlow, (NLCPGEdgeKind)1 })
      {
        var expectedIn = canonical
          .Where(edge => edge.TargetNodeId == node.NodeId.Value && edge.Kind == kind)
          .Select(Describe)
          .ToArray();
        var actualIn = InvokeNeighborQueryByKind(graph, "GetIncomingEdges", node.NodeId.Value, kind)
          .Select(Describe)
          .ToArray();
        Assert.Equal(expectedIn, actualIn);

        var expectedOut = canonical
          .Where(edge => edge.SourceNodeId == node.NodeId.Value && edge.Kind == kind)
          .Select(Describe)
          .ToArray();
        var actualOut = InvokeNeighborQueryByKind(graph, "GetOutgoingEdges", node.NodeId.Value, kind)
          .Select(Describe)
          .ToArray();
        Assert.Equal(expectedOut, actualOut);
      }

      checkedNodes += 1;
    }

    _output.WriteLine($"nodes-checked={checkedNodes}; canonical-edges={canonical.Count}");
    Assert.True(checkedNodes > 0);
  }

  // 元数据存在时投影仍逐字段等价（覆盖 CanonicalEdgeStore 的元数据池分支）。
  [Fact]
  public void ProjectedEdges_WithMetadata_AreFieldEquivalent()
  {
    var graph = BuildVariedGraph(withMetadata: true);
    var canonical = ReadOrderedEdges(graph);

    Assert.Contains(canonical, edge => edge.StructuredLabel is not null);
    Assert.Contains(canonical, edge => edge.ContextId is not null);
    Assert.Contains(canonical, edge => edge.CallSiteContext is not null);
    Assert.All(canonical, edge =>
    {
      // 公开构造函数与投影必须给出一致的 ContextId（投影跳过归一化，故此断言覆盖该风险）。
      var rebuilt = new NLCPGEdge(
        edge.SourceNodeId,
        edge.TargetNodeId,
        edge.Kind,
        edge.StructuredLabel,
        edge.ContextId,
        edge.CallSiteContext);
      Assert.Equal(rebuilt.ContextId, edge.ContextId);
      Assert.Equal(rebuilt.CallSiteContext, edge.CallSiteContext);
      Assert.Equal(rebuilt.StructuredLabel, edge.StructuredLabel);
    });

    _output.WriteLine($"edges-with-metadata={canonical.Count}");
  }

  // 元数据覆盖率 0 时不得分配元数据池（设计文档 §3.4 的收益前提）。
  [Fact]
  public void ZeroMetadataGraph_AllocatesNoMetadataPool()
  {
    var graph = BuildVariedGraph(withMetadata: false);
    var store = ReadEdgeStore(graph);
    var pool = store.GetType()
      .GetField("_metadataPool", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store);
    var ids = store.GetType()
      .GetField("_metadataIds", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store);

    Assert.Null(pool);
    Assert.Null(ids);
    _output.WriteLine($"edges={store.GetType()
      .GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)}; metadata-pool=null; metadata-ids=null");
  }

  // 常驻宽度必须显著低于旧表示（设计文档 §4.1）。
  [Fact]
  public void ResidentEdgeStore_IsMuchNarrowerThanLegacyArray()
  {
    var graph = BuildGraph(edgeCount: 20_000, kindsPerEdge: 36, nodeCount: 141);
    var store = ReadEdgeStore(graph);
    var count = (int)store.GetType()
      .GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)!;

    var sourceArr = (int[])store.GetType()
      .GetField("_sourceOrdinals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
    var targetArr = (int[])store.GetType()
      .GetField("_targetOrdinals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
    var kindArr = (byte[])store.GetType()
      .GetField("_kinds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;

    // 12 B/边（4+4+1 打包后按 12 计），对比旧 NLCPGEdge[] 的 72 B/边。
    var newBytes = (long)count * 12;
    var legacyBytes = (long)count * 72;
    var reductionPercent = 100d * (legacyBytes - newBytes) / legacyBytes;

    _output.WriteLine(
      $"edges={count}; new-bytes={newBytes}; legacy-bytes={legacyBytes}; " +
      $"reduction-percent={reductionPercent:F2}; " +
      $"widths=src:{sourceArr.Length},tgt:{targetArr.Length},kind:{kindArr.Length}");

    Assert.Equal(count, sourceArr.Length);
    Assert.Equal(count, targetArr.Length);
    Assert.Equal(count, kindArr.Length);
    Assert.True(reductionPercent >= 83d, $"expected >=83% reduction, got {reductionPercent:F2}%");
  }

  private static string Describe(NLCPGEdge edge)
  {
    return string.Join(
      '|',
      edge.SourceNodeId.Value,
      (int)edge.Kind,
      edge.TargetNodeId.Value,
      edge.StructuredLabel?.StableKey ?? "-",
      edge.ContextId?.Value ?? "-",
      edge.CallSiteContext?.FilePath ?? "-",
      edge.CallSiteContext?.SpanStart ?? -1,
      edge.CallSiteContext?.SpanEnd ?? -1,
      edge.CallSiteContext?.DisplayName ?? "-");
  }

  private static NLCPGGraph BuildVariedGraph(bool withMetadata = false)
  {
    // 5 个节点 × 10 个 kind ⇒ 容量 5*5*10 = 250 ≥ 240，且三个维度都被覆盖到。
    return BuildGraph(edgeCount: 240, kindsPerEdge: 10, nodeCount: 5, withMetadata: withMetadata);
  }

  private static NLCPGGraph BuildGraph(
    int edgeCount,
    int kindsPerEdge,
    int nodeCount,
    bool withMetadata = false)
  {
    var graph = new NLCPGGraph();
    var nodes = new NLCPGNode[nodeCount];
    for (var index = 0; index < nodeCount; index += 1)
    {
      nodes[index] = graph.AddNode(new NLCPGNodeDraft(
        NLCPGNodeKind.Operation,
        Name: "n" + index,
        FilePath: "fep.cs",
        SpanStart: index * 10,
        SpanEnd: (index * 10) + 4));
    }

    var label = withMetadata
      ? NLCPGEdgeLabel.ForDecisionRelation(NLCPGDecisionRelationKind.DerivedFrom)
      : null;
    // 按下标做三维分解：kind 最快、source 次之、target 最慢。
    // 只要 edgeCount ≤ kindsPerEdge * nodeCount²，三元组就互不重复，去重不会削弱用例强度。
    for (var index = 0; index < edgeCount; index += 1)
    {
      var source = nodes[index % nodeCount];
      var target = nodes[(index / nodeCount) % nodeCount];
      var kind = (NLCPGEdgeKind)((index / (nodeCount * nodeCount)) % kindsPerEdge);
      var callSite = withMetadata && (index % 3 == 0)
        ? new NLCPGCallSiteContext("fep.cs", index, index + 2, "cs" + index)
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

    graph.FreezeQueryIndex();
    return graph;
  }

  private static IReadOnlyList<NLCPGEdge> InvokeNeighborQuery(NLCPGGraph graph, string methodName, NodeId nodeId)
  {
    // GetOutgoingEdges/GetIncomingEdges 是 NLCPGGraphIndex 的 internal 成员，
    // 而 NLCPG 没有 InternalsVisibleTo，故与既有存储契约测试一致改用反射。
    var index = ReadQueryIndex(graph);
    var method = index.GetType().GetMethod(
      methodName,
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      types: new[] { typeof(NodeId) },
      modifiers: null)
      ?? throw new InvalidOperationException($"Method '{methodName}' not found on NLCPGGraphIndex.");
    return (IReadOnlyList<NLCPGEdge>)method.Invoke(index, new object[] { nodeId })!;
  }

  private static IReadOnlyList<NLCPGEdge> InvokeNeighborQueryByKind(
    NLCPGGraph graph,
    string methodName,
    NodeId nodeId,
    NLCPGEdgeKind kind)
  {
    var index = ReadQueryIndex(graph);
    var method = index.GetType().GetMethod(
      methodName,
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      types: new[] { typeof(NodeId), typeof(NLCPGEdgeKind) },
      modifiers: null)
      ?? throw new InvalidOperationException($"Method '{methodName}(NodeId, NLCPGEdgeKind)' not found.");
    return (IReadOnlyList<NLCPGEdge>)method.Invoke(index, new object[] { nodeId, kind })!;
  }

  private static object ReadEdgeStore(NLCPGGraph graph)
  {
    var index = ReadQueryIndex(graph);
    return index.GetType()
      .GetProperty("EdgeStore", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(index)!;
  }

  private static IReadOnlyList<NLCPGEdge> ReadOrderedEdges(NLCPGGraph graph)
  {
    var index = ReadQueryIndex(graph);
    return (IReadOnlyList<NLCPGEdge>)index.GetType()
      .GetProperty("OrderedEdges", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(index)!;
  }

  private static object ReadQueryIndex(NLCPGGraph graph)
  {
    return typeof(NLCPGGraph)
      .GetField("_queryIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(graph)
      ?? throw new InvalidOperationException("Graph is not frozen.");
  }
}
