using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes.ControlFlow.Dominance;

namespace Logic.Analysis.Engine.Passes.ControlFlow.ControlDependenceGraph;

/// <summary>
/// 基于 `PostDominates` 边访问后支配树。
///
/// 这里对应 Joern `CpgPostDomTreeAdapter.scala`。
/// </summary>
public sealed class CpgPostDomTreeAdapter : IDomTreeAdapter<CpgNode>
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 初始化适配器。
    /// </summary>
    public CpgPostDomTreeAdapter(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }


    public CpgNode? GetImmediateDominator(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        CpgEdge? edge = graph.GetIncomingEdges(node.Id, CpgEdgeKind.PostDominates).FirstOrDefault();
        return edge is null ? null : graph.GetNode(edge.SourceId);
    }
}
