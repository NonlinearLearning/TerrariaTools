using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

// G2：固化「边载荷序数化」后的确定性与去重语义（阶段 2 的前置门禁）。
//
// 背景：边缓冲的键从「两端各 28 B 的 StableNodeAnchor」改为「两端各 4 B 的节点序数」。
// 序数按首次入图顺序分配，因此有三类必须守住的契约：
//   1. 跨构建确定性——同一输入连续构建两次，**未排序**的枚举序必须一致；
//   2. 跨并行度确定性——序数分配不得依赖调度；
//   3. 去重语义不变——序数是锚点的单射编码，故「按锚点去重」与「按序数去重」等价。
//
// 断言刻意使用未排序的序列：既有 CpgWorkBatchDeterminismTests 比较排序后的序列，
// 对枚举序不敏感，无法覆盖本次改动的主要风险面。
//
// 观测口径：构图期的 PendingEdgeBuffer 是 internal，故一律先 FreezeQueryIndex()
// 再读公开的 Nodes / Edges。冻结步骤按序数表原序物化边，故 Edges 的**枚举序**
// 正是待降缓冲的枚举序，能在公开 API 上等价观测。
public sealed class PendingEdgeOrdinalizationDeterminismTests
{
  private const string Source = """
    public sealed class OrdinalSample
    {
      public int Produce(int value)
      {
        var result = value;
        if (value > 0)
        {
          result += 1;
        }
        else
        {
          result -= 1;
        }

        return result;
      }

      public int Consume(int value)
      {
        return Produce(value);
      }
    }
    """;

  // 同一输入构建两次：未排序的节点序与边序必须逐位相同。
  [Fact]
  public void BuildFromSource_RepeatedBuilds_PreserveUnsortedEnumerationOrder()
  {
    var first = Capture();
    var second = Capture();

    Assert.NotEmpty(first.FrozenEdgeOrder);
    Assert.NotEmpty(first.FrozenNodeOrder);
    Assert.Equal(first.FrozenNodeOrder, second.FrozenNodeOrder);
    Assert.Equal(first.FrozenEdgeOrder, second.FrozenEdgeOrder);
  }

  // 未排序的枚举序必须在不同并行度下相同，否则结果依赖调度。
  [Fact]
  public void BuildFromSource_AcrossParallelism_PreserveUnsortedEnumerationOrder()
  {
    var baseline = Capture(dop: 1);
    Assert.NotEmpty(baseline.FrozenEdgeOrder);

    foreach (var dop in new[] { 2, 4, 16 })
    {
      var actual = Capture(dop);
      Assert.Equal(baseline.FrozenNodeOrder, actual.FrozenNodeOrder);
      Assert.Equal(baseline.FrozenEdgeOrder, actual.FrozenEdgeOrder);
    }
  }

  // 元素数守恒：序数化不得丢边，也不得凭空造边。
  [Fact]
  public void BuildFromSource_FrozenEdgeCount_MatchesBuildMetrics()
  {
    var builder = CreateBuilder();
    var graph = builder.BuildFromSource(Source, "ordinalization-count.cs");

    Assert.Equal(builder.LastBuildMetrics.EdgeCount, graph.Edges.Count);
    Assert.NotEmpty(graph.Edges);
  }

  // 构图期枚举序 == 插入序（序数分配的直接后果），且跨构建逐位相同。
  [Fact]
  public void AddEdge_EnumerationOrder_EqualsInsertionOrder()
  {
    var first = CapturePending();
    var second = CapturePending();

    Assert.Equal(first.InsertionOrder, first.PendingOrder);
    Assert.Equal(first.InsertionOrder, second.PendingOrder);
  }

  // 去重：同一对端点 + 同一 kind 的重复加边只保留一条。
  [Fact]
  public void AddEdge_DuplicateSameEndpointsAndKind_KeepsSingleEdge()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));

    graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow);
    graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow);
    graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow);
    graph.FreezeQueryIndex();

    Assert.Single(graph.Edges);
  }

  // 去重：元数据不同的同端点同 kind 边必须各自保留（元数据 id 参与键）。
  [Fact]
  public void AddEdge_DifferingStructuredLabels_KeepsAllEdges()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));

    graph.AddEdge(source, target, NLCPGEdgeKind.InterproceduralDataFlow);
    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.InterproceduralDataFlow,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter));
    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.InterproceduralDataFlow,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ReturnToMethodReturn));
    graph.FreezeQueryIndex();

    Assert.Equal(3, graph.Edges.Count);
  }

  // 去重：元数据按值比较，内容相同但实例不同的标签只保留一条。
  [Fact]
  public void AddEdge_EqualButDistinctLabelInstances_KeepSingleEdge()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));

    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.InterproceduralDataFlow,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter));
    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.InterproceduralDataFlow,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter));
    graph.FreezeQueryIndex();

    Assert.Single(graph.Edges);
  }

  // 去重：上下文 ID 不同则视为不同边。
  [Fact]
  public void AddEdge_DifferingContextIds_KeepsBothEdges()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));

    graph.AddEdge(source, target, NLCPGEdgeKind.CallTargets, contextId: new NLCPGContextId("ctx-a"));
    graph.AddEdge(source, target, NLCPGEdgeKind.CallTargets, contextId: new NLCPGContextId("ctx-b"));
    graph.FreezeQueryIndex();

    Assert.Equal(2, graph.Edges.Count);
  }

  // 冻结期的 ContextId 插值次数必须随【不同元数据数】增长，而不是随【边数】增长。
  //
  // 为什么不能按值/按引用读 graph.Edges 来断言：冻结后 Edges 由 CanonicalEdgeStore 投影，
  // 而该存储的元数据池在 BuildMetadataRanks 里又按【值】去重了一次，会把逐边各自插值出的
  // 重复字符串重新折叠成共享实例。故无论修复前后，冻结后的读取都看到共享实例——
  // 曾经写过一版 Assert.Same 断言，在**回退修复后依然通过**，属于零鉴别力的假护栏。
  //
  // 【为什么基准必须取"零元数据同边数图"，而不是"多调用点图"】
  // 若对比"512 条边共用 1 个调用点"与"512 条边各用 1 个调用点"，两者插值次数之差是
  // 511（修复后：1 vs 512；修复前：513 vs 1024），**差值不随修复改变**，因此
  // 任何形如 f(多调用点) − f(少调用点) 的判据都恒等成立、无法鉴别。
  // 真正的观测面是"同一形状下插值次数本身"：把基准换成**不带任何元数据**、边数与
  // NLCPGEdge[] 完全相同的一张图，池开销同为 0 条，于是
  //   A − B ≈ 插值次数 × 每次插值字节数
  // 修复后 A 只插值 1 次，修复前插值 513 次，差距约 512 次插值。
  [Fact]
  public void FreezeQueryIndex_ContextIdInterpolation_ScalesWithMetadataNotEdgeCount()
  {
    // A：512 条边共用 1 个调用点（1 条池条目）。
    var withContext = MeasureFreezeAllocation(distinctCallSites: 1);
    // B：同样 512 条边，但完全不设调用点（0 条池条目）。
    var withoutContext = MeasureFreezeAllocation(distinctCallSites: 0);

    var overhead = withContext - withoutContext;
    Assert.True(
      overhead < 100_000,
      $"单次元数据带来 {overhead} B 冻结分配（基准 {withoutContext} B / 含元数据 {withContext} B）："
      + "ContextId 很可能仍按每条边插值一次，而不是每个不同元数据插值一次。");
  }

  private const int EdgeCount = 512;

  // 量测同一形状图的 FreezeQueryIndex 分配量；建图本身不计入。
  // distinctCallSites == 0 表示不设调用点（零元数据基准图）。
  private static long MeasureFreezeAllocation(int distinctCallSites)
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    // 600 字符：让插值串的分配量压过每元数据的固有开销（池条目/字典槽约 130 B）。
    var padding = new string('d', 600);

    for (var index = 0; index < EdgeCount; index += 1)
    {
      var target = graph.AddNode(new NLCPGNodeDraft(
        NLCPGNodeKind.Operation,
        Name: $"target{index}"));
      if (distinctCallSites == 0)
      {
        graph.AddEdge(source, target, NLCPGEdgeKind.CallTargets);
        continue;
      }

      var callSiteIndex = distinctCallSites == 1 ? 0 : index;
      graph.AddEdge(
        source,
        target,
        NLCPGEdgeKind.CallTargets,
        callSiteContext: new NLCPGCallSiteContext(
          "allocation-probe.cs",
          callSiteIndex,
          callSiteIndex + 1,
          $"Caller.Method{callSiteIndex:D4}.{padding}"));
    }

    var before = GC.GetAllocatedBytesForCurrentThread();
    graph.FreezeQueryIndex();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.Equal(EdgeCount, graph.Edges.Count);
    return allocated;
  }

  // 序数必须区分方向：自环与反向边都不得被折叠。
  [Fact]
  public void AddEdge_OppositeDirectionAndSelfLoop_AreDistinct()
  {
    var graph = new NLCPGGraph();
    var first = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "first"));
    var second = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "second"));

    graph.AddEdge(first, second, NLCPGEdgeKind.DataFlow);
    graph.AddEdge(second, first, NLCPGEdgeKind.DataFlow);
    graph.AddEdge(first, first, NLCPGEdgeKind.DataFlow);
    graph.FreezeQueryIndex();

    Assert.Equal(3, graph.Edges.Count);
  }

  // 序数稳定性：重复入图同一锚点不新增节点。
  [Fact]
  public void AddNode_RepeatedSameAnchor_DoesNotGrowNodeSet()
  {
    var graph = new NLCPGGraph();
    var draft = new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "stable");

    var first = graph.AddNode(draft);
    var second = graph.AddNode(draft);

    Assert.Single(graph.Nodes);
    Assert.Equal(first.StableAnchor, second.StableAnchor);
  }

  private static NLCPGBuilder CreateBuilder(int dop = 1)
  {
    return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = dop,
      RequestedCapabilities = new[] { NLCPGCapability.All },
      LargeFileLineThreshold = 1,
      LargeFileMethodThreshold = 1,
      LargeMethodLineSpanThreshold = 1,
      SyntaxLargeFileLineThreshold = 1,
    });
  }

  private static GraphCapture Capture(int dop = 1)
  {
    var graph = CreateBuilder(dop).BuildFromSource(Source, "ordinalization-determinism.cs");

    return new GraphCapture(
      graph.Nodes.Select(node => $"{node.NodeId}|{node.StableAnchor}").ToArray(),
      graph.Edges
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray());
  }

  // 手工加边，记录插入序与冻结后的枚举序，验证两者逐位一致。
  private static PendingCapture CapturePending()
  {
    const int NodeCount = 12;
    var graph = new NLCPGGraph();
    var nodes = new List<NLCPGNode>();
    for (var index = 0; index < NodeCount; index += 1)
    {
      nodes.Add(graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: $"n{index}")));
    }

    var insertion = new List<(NLCPGNode Source, NLCPGNode Target)>();
    for (var index = 0; index < nodes.Count; index += 1)
    {
      var source = nodes[index];
      var target = nodes[(index + 1) % nodes.Count];
      graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow);
      insertion.Add((source, target));
    }

    graph.FreezeQueryIndex();

    // 冻结后按 NodeId 无法还原插入序，故比较"端点锚点对"的顺序。
    var insertionOrder = insertion
      .Select(pair => $"{pair.Source.StableAnchor}|{pair.Target.StableAnchor}")
      .ToArray();
    var pendingOrder = graph.Edges
      .Select(edge => DescribeFrozenEdge(graph, edge))
      .ToArray();

    return new PendingCapture(insertionOrder, pendingOrder);
  }

  private static string DescribeFrozenEdge(NLCPGGraph graph, NLCPGEdge edge)
  {
    var source = graph.GetNode(edge.SourceNodeId)
      ?? throw new InvalidOperationException($"Missing frozen source node {edge.SourceNodeId}.");
    var target = graph.GetNode(edge.TargetNodeId)
      ?? throw new InvalidOperationException($"Missing frozen target node {edge.TargetNodeId}.");
    return $"{source.StableAnchor}|{target.StableAnchor}";
  }

  private sealed record GraphCapture(
    IReadOnlyList<string> FrozenNodeOrder,
    IReadOnlyList<string> FrozenEdgeOrder);

  private sealed record PendingCapture(
    IReadOnlyList<string> InsertionOrder,
    IReadOnlyList<string> PendingOrder);
}
