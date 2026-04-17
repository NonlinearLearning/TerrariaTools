using Analysis.Core;

namespace Analysis.Passes.ControlFlow.Dominance;

/// <summary>
/// 基于 CFG 计算直接支配与直接后支配边。
/// </summary>
public sealed class CfgDominatorPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        CpgGraph graph = builder.Graph;
        CpgCfgAdapter cfgAdapter = new(graph);
        ReverseCpgCfgAdapter reverseCfgAdapter = new(graph);

        foreach (CpgNode methodNode in graph.GetNodes(CpgNodeKind.Method).ToArray())
        {
            IReadOnlyDictionary<CpgNode, CpgNode> dominators =
                new CfgDominator<CpgNode>(cfgAdapter).Calculate(methodNode);
            foreach ((CpgNode node, CpgNode immediateDominator) in dominators)
            {
                EnsureEdge(builder, immediateDominator.Id, node.Id, CpgEdgeKind.Dominates);
            }

            CpgNode? methodReturnNode = FindMethodReturnNode(graph, methodNode);
            if (methodReturnNode is null)
            {
                continue;
            }

            IReadOnlyDictionary<CpgNode, CpgNode> postDominators =
                new CfgDominator<CpgNode>(reverseCfgAdapter).Calculate(methodReturnNode);
            foreach ((CpgNode node, CpgNode immediatePostDominator) in postDominators)
            {
                EnsureEdge(builder, immediatePostDominator.Id, node.Id, CpgEdgeKind.PostDominates);
            }
        }
    }

    private static CpgNode? FindMethodReturnNode(CpgGraph graph, CpgNode methodNode)
    {
        return graph.GetNodes(CpgNodeKind.MethodReturn)
            .FirstOrDefault(node =>
                node.TryGetProperty<long>("AstParentId", out long parentId) &&
                parentId == methodNode.Id);
    }

    private static void EnsureEdge(CpgGraphBuilder builder, long sourceId, long targetId, CpgEdgeKind edgeKind)
    {
        if (builder.Graph.GetOutgoingEdges(sourceId, edgeKind).Any(edge => edge.TargetId == targetId))
        {
            return;
        }

        builder.AddEdge(sourceId, targetId, edgeKind);
    }
}
