using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes.ControlFlow.Dominance;

/// <summary>
/// 使用反向 CFG 边访问图。
/// </summary>
public sealed class ReverseCpgCfgAdapter : ICfgAdapter<CpgNode>
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 初始化反向适配器。
    /// </summary>
    public ReverseCpgCfgAdapter(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }


    public IEnumerable<CpgNode> GetSuccessors(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetIncomingEdges(node.Id, CpgEdgeKind.Cfg).Select(edge => graph.GetNode(edge.SourceId));
    }


    public IEnumerable<CpgNode> GetPredecessors(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Cfg).Select(edge => graph.GetNode(edge.TargetId));
    }
}
