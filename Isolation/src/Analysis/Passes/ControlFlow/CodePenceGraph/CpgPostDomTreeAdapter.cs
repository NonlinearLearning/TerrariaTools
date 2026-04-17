using Analysis.Core;
using Analysis.Passes.ControlFlow.Dominance;

namespace Analysis.Passes.ControlFlow.CodePenceGraph;

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

    /// <inheritdoc />
    public CpgNode? GetImmediateDominator(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        CpgEdge? edge = graph.GetIncomingEdges(node.Id, CpgEdgeKind.PostDominates).FirstOrDefault();
        return edge is null ? null : graph.GetNode(edge.SourceId);
    }
}
