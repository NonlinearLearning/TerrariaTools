using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes.ControlFlow.ControlDependenceGraph;
using Logic.Analysis.Engine.Passes.ControlFlow.Dominance;

namespace Logic.Analysis.Engine.Passes.ControlFlow;

/// <summary>
/// 基于后支配边界构建控制依赖边。
///
/// 当前实现参考 Joern `codepencegraph/CdgPass.scala`：
/// - 先基于反向 CFG 和后支配树计算后支配边界；
/// - 再从边界节点指向受其控制的节点补 `CDG` 边。
/// </summary>
public sealed class BuildCdgPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        ReverseCpgCfgAdapter reverseCfgAdapter = new(builder.Graph);
        CpgPostDomTreeAdapter postDomTreeAdapter = new(builder.Graph);
        CfgDominatorFrontier<CpgNode> frontierCalculator = new(reverseCfgAdapter, postDomTreeAdapter);

        foreach (CpgNode methodNode in builder.Graph.GetNodes(CpgNodeKind.Method).ToArray())
        {
            IReadOnlyCollection<CpgNode> cfgNodes = GetCfgNodesForMethod(builder.Graph, methodNode);
            IReadOnlyDictionary<CpgNode, IReadOnlyCollection<CpgNode>> frontier =
                frontierCalculator.Calculate(cfgNodes.Prepend(methodNode));

            foreach ((CpgNode node, IReadOnlyCollection<CpgNode> frontierNodes) in frontier)
            {
                foreach (CpgNode frontierNode in frontierNodes.Where(IsCdgSourceNode))
                {
                    EnsureEdge(builder, frontierNode.Id, node.Id, CpgEdgeKind.Cdg);
                }
            }
        }
    }

    private static IReadOnlyCollection<CpgNode> GetCfgNodesForMethod(CpgGraph graph, CpgNode methodNode)
    {
        List<CpgNode> nodes = new();
        Queue<long> workQueue = new();
        HashSet<long> visited = new();

        foreach (CpgEdge containsEdge in graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Contains))
        {
            workQueue.Enqueue(containsEdge.TargetId);
        }

        while (workQueue.Count > 0)
        {
            long currentId = workQueue.Dequeue();
            if (!visited.Add(currentId))
            {
                continue;
            }

            CpgNode currentNode = graph.GetNode(currentId);
            nodes.Add(currentNode);

            foreach (CpgEdge containsEdge in graph.GetOutgoingEdges(currentId, CpgEdgeKind.Contains))
            {
                workQueue.Enqueue(containsEdge.TargetId);
            }
        }

        return nodes;
    }

    private static bool IsCdgSourceNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Literal
            or CpgNodeKind.Identifier
            or CpgNodeKind.Call
            or CpgNodeKind.MethodRef
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.Block
            or CpgNodeKind.Method;
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
