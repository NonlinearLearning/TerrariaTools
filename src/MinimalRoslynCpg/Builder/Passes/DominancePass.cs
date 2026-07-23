using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes
{

internal sealed class DominancePass : IRoslynCpgPass
{
    internal static DominancePass Instance { get; } = new();

    private DominancePass()
    {
    }

    public string Name => nameof(DominancePass);

    public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
    {
        builder.RunDominancePass(context);
    }
}

}

namespace MinimalRoslynCpg.Builder
{

public sealed partial class RoslynCpgBuilder
{
    private readonly List<DominanceMethodOverlay> _dominanceOverlays = new();

    private sealed record DominanceMethodOverlay(
        ControlFlowGraph ControlFlowGraph,
        IReadOnlyDictionary<int, IReadOnlySet<int>> DominatorsByBlockOrdinal,
        IReadOnlyDictionary<int, IReadOnlySet<int>> PostDominatorsByBlockOrdinal,
        IReadOnlyDictionary<int, int?> ImmediatePostDominatorByBlockOrdinal,
        IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> NodesByBlockOrdinal,
        IReadOnlyDictionary<int, RoslynCpgNode?> ControlNodesByBlockOrdinal);

    internal void RunDominancePass(RoslynCpgBuildContext context)
    {
        _dominanceOverlays.Clear();

        foreach (var rootPlan in GetOperationRootPlans(context.Root, context.SemanticModel))
        {
            if (context.SemanticModel.GetOperation(rootPlan.BodySyntax) is not IBlockOperation methodBlock ||
                !IsMethodRootBlock(methodBlock) ||
                rootPlan.OwningMethod is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var controlFlowGraph = CreateControlFlowGraph(methodBlock);
            if (controlFlowGraph is null)
            {
                continue;
            }
            var nodesByBlockOrdinal = MapNodesByBlockOrdinal(controlFlowGraph, methodSymbol, context.Graph);
            var controlNodesByBlockOrdinal = MapControlNodesByBlockOrdinal(controlFlowGraph, nodesByBlockOrdinal);
            var successorsByBlockOrdinal = BuildSuccessorsByBlockOrdinal(controlFlowGraph);
            var predecessorsByBlockOrdinal = ReverseNeighbors(successorsByBlockOrdinal);
            var entryOrdinal = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Entry).Ordinal;
            var exitOrdinal = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Exit).Ordinal;
            var dominators = CalculateDominators(
                successorsByBlockOrdinal,
                predecessorsByBlockOrdinal,
                entryOrdinal);
            var postDominators = CalculateDominators(
                predecessorsByBlockOrdinal,
                successorsByBlockOrdinal,
                exitOrdinal);
            var immediatePostDominators = CalculateImmediatePostDominators(postDominators);

            AddOverlayEdges(nodesByBlockOrdinal, dominators, RoslynCpgEdgeKind.Dominates, context.Graph);
            AddPostDominanceEdges(nodesByBlockOrdinal, postDominators, context.Graph);
            _dominanceOverlays.Add(new DominanceMethodOverlay(
                controlFlowGraph,
                dominators,
                postDominators,
                immediatePostDominators,
                nodesByBlockOrdinal,
                controlNodesByBlockOrdinal));
        }
    }

    private Dictionary<int, IReadOnlyList<RoslynCpgNode>> MapNodesByBlockOrdinal(
        ControlFlowGraph controlFlowGraph,
        IMethodSymbol methodSymbol,
        RoslynCpgGraph graph)
    {
        var nodesByBlockOrdinal = new Dictionary<int, IReadOnlyList<RoslynCpgNode>>();
        foreach (var block in controlFlowGraph.Blocks)
        {
            var nodes = new HashSet<RoslynCpgNode>();
            if (block.Kind == BasicBlockKind.Entry)
            {
                nodes.Add(GetOrCreateMethodEntryNode(methodSymbol, graph));
            }
            else if (block.Kind == BasicBlockKind.Exit)
            {
                nodes.Add(GetOrCreateMethodExitNode(methodSymbol, graph));
            }

            foreach (var operation in block.Operations)
            {
                foreach (var descendant in operation.DescendantsAndSelf())
                {
                    AddMappedOperationNode(descendant, nodes, graph);
                }
            }

            if (block.BranchValue is not null)
            {
                foreach (var descendant in block.BranchValue.DescendantsAndSelf())
                {
                    AddMappedOperationNode(descendant, nodes, graph);
                }
            }

            nodesByBlockOrdinal[block.Ordinal] = nodes
                .OrderBy(node => node.NodeId)
                .ThenBy(node => node.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        return nodesByBlockOrdinal;
    }

    private static ControlFlowGraph? CreateControlFlowGraph(IBlockOperation methodBlock)
    {
        return methodBlock.Parent switch
        {
            IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody),
            IConstructorBodyOperation constructorBody => ControlFlowGraph.Create(constructorBody),
            _ => null,
        };
    }

    private void AddMappedOperationNode(
        IOperation operation,
        ISet<RoslynCpgNode> nodes,
        RoslynCpgGraph graph)
    {
        nodes.Add(GetOrCreateOperationNode(operation, graph));
    }

    private static Dictionary<int, RoslynCpgNode?> MapControlNodesByBlockOrdinal(
        ControlFlowGraph controlFlowGraph,
        IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> nodesByBlockOrdinal)
    {
        var controlNodesByBlockOrdinal = new Dictionary<int, RoslynCpgNode?>();
        foreach (var block in controlFlowGraph.Blocks)
        {
            controlNodesByBlockOrdinal[block.Ordinal] = nodesByBlockOrdinal[block.Ordinal]
                .OrderBy(node => GetControlNodeKindPriority(node.Kind))
                .ThenByDescending(node => (node.SpanEnd ?? int.MinValue) - (node.SpanStart ?? int.MaxValue))
                .ThenBy(node => node.NodeId)
                .FirstOrDefault();
        }

        return controlNodesByBlockOrdinal;
    }

    private static int GetControlNodeKindPriority(RoslynCpgNodeKind kind)
    {
        return kind switch
        {
            RoslynCpgNodeKind.OpBinary => 1,
            RoslynCpgNodeKind.OpConditional => 2,
            RoslynCpgNodeKind.Operation => 3,
            _ => 4
        };
    }

    private static Dictionary<int, IReadOnlySet<int>> BuildSuccessorsByBlockOrdinal(ControlFlowGraph controlFlowGraph)
    {
        var successorsByBlockOrdinal = controlFlowGraph.Blocks.ToDictionary(
            block => block.Ordinal,
            _ => (IReadOnlySet<int>)new SortedSet<int>());
        foreach (var block in controlFlowGraph.Blocks)
        {
            var successors = (SortedSet<int>)successorsByBlockOrdinal[block.Ordinal];
            AddDestination(block.FallThroughSuccessor, successors);
            AddDestination(block.ConditionalSuccessor, successors);
        }

        return successorsByBlockOrdinal;
    }

    private static void AddDestination(ControlFlowBranch? branch, ISet<int> successors)
    {
        if (branch?.Destination is not null)
        {
            successors.Add(branch.Destination.Ordinal);
        }
    }

    private static Dictionary<int, IReadOnlySet<int>> ReverseNeighbors(
        IReadOnlyDictionary<int, IReadOnlySet<int>> successorsByBlockOrdinal)
    {
        var predecessors = successorsByBlockOrdinal.Keys.ToDictionary(
            ordinal => ordinal,
            _ => (IReadOnlySet<int>)new SortedSet<int>());
        foreach (var (sourceOrdinal, successors) in successorsByBlockOrdinal)
        {
            foreach (var targetOrdinal in successors)
            {
                ((SortedSet<int>)predecessors[targetOrdinal]).Add(sourceOrdinal);
            }
        }

        return predecessors;
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<int>> CalculateDominators(
        IReadOnlyDictionary<int, IReadOnlySet<int>> successorsByBlockOrdinal,
        IReadOnlyDictionary<int, IReadOnlySet<int>> predecessorsByBlockOrdinal,
        int rootOrdinal)
    {
        var reachable = CalculateReversePostOrder(successorsByBlockOrdinal, rootOrdinal);
        var reachableSet = reachable.ToHashSet();
        var allReachable = new SortedSet<int>(reachable);
        var dominators = successorsByBlockOrdinal.Keys.ToDictionary(
            ordinal => ordinal,
            ordinal => ordinal == rootOrdinal
                ? (IReadOnlySet<int>)new SortedSet<int> { ordinal }
                : reachableSet.Contains(ordinal)
                    ? new SortedSet<int>(allReachable)
                    : new SortedSet<int> { ordinal });

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var ordinal in reachable.Where(ordinal => ordinal != rootOrdinal))
            {
                var reachablePredecessors = predecessorsByBlockOrdinal[ordinal]
                    .Where(reachableSet.Contains)
                    .OrderBy(value => value)
                    .ToArray();
                if (reachablePredecessors.Length == 0)
                {
                    continue;
                }

                var intersection = new SortedSet<int>(dominators[reachablePredecessors[0]]);
                foreach (var predecessor in reachablePredecessors.Skip(1))
                {
                    intersection.IntersectWith(dominators[predecessor]);
                }

                intersection.Add(ordinal);
                if (!intersection.SetEquals(dominators[ordinal]))
                {
                    dominators[ordinal] = intersection;
                    changed = true;
                }
            }
        }

        return dominators;
    }

    private static IReadOnlyList<int> CalculateReversePostOrder(
        IReadOnlyDictionary<int, IReadOnlySet<int>> successorsByBlockOrdinal,
        int rootOrdinal)
    {
        var visited = new HashSet<int>();
        var postOrder = new List<int>();
        Visit(rootOrdinal);
        postOrder.Reverse();
        return postOrder;

        void Visit(int ordinal)
        {
            if (!visited.Add(ordinal))
            {
                return;
            }

            foreach (var successor in successorsByBlockOrdinal[ordinal].OrderBy(value => value))
            {
                Visit(successor);
            }

            postOrder.Add(ordinal);
        }
    }

    private static IReadOnlyDictionary<int, int?> CalculateImmediatePostDominators(
        IReadOnlyDictionary<int, IReadOnlySet<int>> postDominatorsByBlockOrdinal)
    {
        var immediatePostDominators = new Dictionary<int, int?>();
        foreach (var (ordinal, postDominators) in postDominatorsByBlockOrdinal)
        {
            immediatePostDominators[ordinal] = postDominators
                .Where(candidate => candidate != ordinal)
                .OrderByDescending(candidate => postDominatorsByBlockOrdinal[candidate].Count)
                .ThenBy(candidate => candidate)
                .Select(candidate => (int?)candidate)
                .FirstOrDefault();
        }

        return immediatePostDominators;
    }

    private static void AddOverlayEdges(
        IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> nodesByBlockOrdinal,
        IReadOnlyDictionary<int, IReadOnlySet<int>> relationsByTargetBlockOrdinal,
        RoslynCpgEdgeKind edgeKind,
        RoslynCpgGraph graph)
    {
        foreach (var (targetOrdinal, sourceOrdinals) in relationsByTargetBlockOrdinal)
        {
            foreach (var sourceOrdinal in sourceOrdinals.Where(sourceOrdinal => sourceOrdinal != targetOrdinal))
            {
                foreach (var sourceNode in nodesByBlockOrdinal[sourceOrdinal])
                {
                    foreach (var targetNode in nodesByBlockOrdinal[targetOrdinal])
                    {
                        graph.AddEdge(sourceNode, targetNode, edgeKind);
                    }
                }
            }
        }
    }

    private static void AddPostDominanceEdges(
        IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> nodesByBlockOrdinal,
        IReadOnlyDictionary<int, IReadOnlySet<int>> postDominatorsByBlockOrdinal,
        RoslynCpgGraph graph)
    {
        foreach (var (sourceOrdinal, postDominatorOrdinals) in postDominatorsByBlockOrdinal)
        {
            foreach (var targetOrdinal in postDominatorOrdinals.Where(targetOrdinal => targetOrdinal != sourceOrdinal))
            {
                foreach (var sourceNode in nodesByBlockOrdinal[sourceOrdinal])
                {
                    foreach (var targetNode in nodesByBlockOrdinal[targetOrdinal])
                    {
                        graph.AddEdge(sourceNode, targetNode, RoslynCpgEdgeKind.PostDominates);
                    }
                }
            }
        }
    }
}

}
