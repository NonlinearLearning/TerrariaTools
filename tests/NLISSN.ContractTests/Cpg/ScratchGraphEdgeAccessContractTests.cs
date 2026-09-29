using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

// scratch 图取数通道（EnumerateScratchEdges）的定向契约。
//
// 背景：ControlFlow / Dominance / ControlDependence 三个 pass 各建一张 worker 局部图
// （scratch 图），其唯一出口是 LocalCpgFragment（节点描述符 + 边候选）。此前它们经
// FreezeQueryIndex() 后读 Nodes/Edges 取数，即为这条出口付了整份查询索引的代价，
// 而那份索引随即随图变成垃圾。现改为经具名 scratch 通道直接读 pending 边。
//
// 本文件守住三件既有测试看不见的事：
//   ① 【等价】未冻结的 scratch 通道与冻结后的 Edges 路径产出**同序同内容**的边；
//      这是本项"改名不重要、换数据源才重要"的核心判据。
//   ② 【fail-closed】冻结后调用 scratch 通道必须抛错，而不是静默返回空集。
//      这不是洁癖：Edges 在未冻结时正是【静默返回空数组】，删除 freeze 而不换数据源
//      会无声产出空 fragment。故本通道必须把该失效形态转成异常。
//   ③ 【守卫精确性】携带 CallSiteContext 时两路会分叉，必须拒绝；
//      而仅有显式 ContextId（两路其实一致）**不得**被误拒。
public sealed class ScratchGraphEdgeAccessContractTests
{
  // ── ① 等价：scratch 通道 vs 冻结后的 Edges ────────────────────────────────────

  // 核心判据：同一张图，未冻结时经 scratch 通道取到的边序列，
  // 与冻结后经 Edges 取到的边序列**逐元素相同**（含顺序与全部字段）。
  //
  // 为什么必须逐字段比较：NLCPGNode.Equals 在带 StableAnchor 时只看锚点，
  // 用 record 的默认 Equals 比较 PendingEdge 会让本测试退化（锚点相同即相等，
  // 掩盖字段差异）。故显式展开每个字段。
  [Fact]
  public void ScratchEdges_MatchFrozenEdgesPath_ElementForElement()
  {
    var graph = new NLCPGGraph();
    var first = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "first"));
    var second = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "second"));
    var third = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "third"));

    // 与生产 scratch 同形：全部 3 参 AddEdge（无元数据），且含重复边以覆盖去重语义。
    graph.AddEdge(first, second, NLCPGEdgeKind.CfgNext);
    graph.AddEdge(second, third, NLCPGEdgeKind.CfgTrue);
    graph.AddEdge(first, second, NLCPGEdgeKind.CfgNext);
    graph.AddEdge(third, first, NLCPGEdgeKind.CfgFalse);

    var scratch = DescribeScratchEdges(graph);
    graph.FreezeQueryIndex();
    var frozen = DescribeFrozenEdges(graph);

    Assert.NotEmpty(scratch);
    Assert.Equal(frozen, scratch);
  }

  // 节点侧等价：未冻结的 Nodes 与冻结后的 Nodes 枚举序必须一致。
  // 片段的节点描述符按此序产出，序变则 fragment 内容变。
  [Fact]
  public void ScratchNodes_MatchFrozenNodesPath_ElementForElement()
  {
    var graph = new NLCPGGraph();
    graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "alpha"));
    graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "beta"));
    graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "gamma"));

    var before = graph.Nodes.Select(node => node.StableAnchor).ToArray();
    graph.FreezeQueryIndex();
    var after = graph.Nodes.Select(node => node.StableAnchor).ToArray();

    Assert.Equal(before, after);
  }

  // 生产 scratch 的元数据形状：全部 3 参 AddEdge ⇒ 元数据必须全为 null。
  // 这是 §"两条路径当前都给出 null" 这一前提的可失败化。
  [Fact]
  public void ScratchEdges_CarryNoMetadata_ForProductionScratchShape()
  {
    var graph = new NLCPGGraph();
    var first = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "first"));
    var second = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "second"));
    graph.AddEdge(first, second, NLCPGEdgeKind.ControlDependence);
    graph.AddEdge(second, first, NLCPGEdgeKind.PostDominates);

    var edges = graph.EnumerateScratchEdges().ToArray();

    Assert.NotEmpty(edges);
    Assert.All(edges, edge =>
    {
      Assert.Null(edge.StructuredLabel);
      Assert.Null(edge.ContextId);
      Assert.Null(edge.CallSiteContext);
    });
  }

  // ── ② fail-closed：冻结后必须抛错 ────────────────────────────────────────────

  // 冻结会 Release pending 缓冲，此后读 scratch 通道必须抛错。
  // 若本断言失败（改为静默返回空集），说明 scratch 图会被无声地读成空图。
  [Fact]
  public void EnumerateScratchEdges_AfterFreeze_Throws()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));
    graph.AddEdge(source, target, NLCPGEdgeKind.CfgNext);
    graph.FreezeQueryIndex();

    Assert.True(graph.HasQueryIndex);
    var exception = Assert.Throws<InvalidOperationException>(
      () => graph.EnumerateScratchEdges().ToArray());
    Assert.Contains("未冻结", exception.Message, StringComparison.Ordinal);
  }

  // ── ③ 守卫精确性：只拒 CallSiteContext，不误拒显式 ContextId ──────────────────

  // 携带 CallSiteContext ⇒ 两路分叉（冻结路径给出 ToContextId() 插值值，pending 路径给出原始值）
  // ⇒ 必须 fail-closed，而不是静默给出不同的 ContextId。
  [Fact]
  public void EnumerateScratchEdges_WithCallSiteContext_Throws()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));
    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.CallTargets,
      callSiteContext: new NLCPGCallSiteContext("scratch-guard.cs", 1, 2, "Caller.Run"));

    var exception = Assert.Throws<InvalidOperationException>(
      () => graph.EnumerateScratchEdges().ToArray());
    Assert.Contains("CallSiteContext", exception.Message, StringComparison.Ordinal);
  }

  // 仅带显式 ContextId 时两路其实一致（resolved == 原始值）⇒ **不得**被误拒。
  // 本用例防止"守卫写宽了"把合法形状一并挡掉。
  [Fact]
  public void EnumerateScratchEdges_WithExplicitContextIdOnly_IsAccepted()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));
    graph.AddEdge(source, target, NLCPGEdgeKind.CallTargets, contextId: new NLCPGContextId("ctx-scratch"));

    var scratch = DescribeScratchEdges(graph);
    graph.FreezeQueryIndex();
    var frozen = DescribeFrozenEdges(graph);

    Assert.Equal(frozen, scratch);
  }

  // ── 观测辅助 ─────────────────────────────────────────────────────────────────

  // 逐字段展开：不依赖 PendingEdge 的 record Equals（其 NLCPGNode 字段只看锚点）。
  private static string[] DescribeScratchEdges(NLCPGGraph graph)
  {
    return graph.EnumerateScratchEdges()
      .Select(Describe)
      .ToArray();
  }

  private static string[] DescribeFrozenEdges(NLCPGGraph graph)
  {
    var nodesById = graph.Nodes
      .Where(node => node.NodeId.HasValue)
      .ToDictionary(node => node.NodeId!.Value);

    return graph.Edges
      .Select(edge =>
      {
        var source = nodesById[edge.SourceNodeId];
        var target = nodesById[edge.TargetNodeId];
        return Describe(new NLCPGGraph.PendingEdge(
          source,
          target,
          edge.Kind,
          edge.StructuredLabel,
          edge.ContextId,
          edge.CallSiteContext));
      })
      .ToArray();
  }

  private static string Describe(NLCPGGraph.PendingEdge edge)
  {
    return string.Join(
      "|",
      edge.SourceNode.StableAnchor?.ToString() ?? "<null>",
      edge.SourceNode.NameId,
      edge.TargetNode.StableAnchor?.ToString() ?? "<null>",
      edge.TargetNode.NameId,
      edge.Kind,
      edge.StructuredLabel?.ToString() ?? "<null>",
      edge.ContextId?.Value ?? "<null>",
      edge.CallSiteContext?.ToContextId().Value ?? "<null>");
  }
}
