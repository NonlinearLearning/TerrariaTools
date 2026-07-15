using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Builder.Passes
{

internal sealed class ControlDependencePass : IRoslynCpgPass
{
    internal static ControlDependencePass Instance { get; } = new();

    private ControlDependencePass()
    {
    }

    public string Name => nameof(ControlDependencePass);

    public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
    {
        builder.RunControlDependencePass(context);
    }
}

}

namespace MinimalRoslynCpg.Builder
{

public sealed partial class RoslynCpgBuilder
{
    internal void RunControlDependencePass(RoslynCpgBuildContext context)
    {
        foreach (var overlay in _dominanceOverlays)
        {
            foreach (var block in overlay.ControlFlowGraph.Blocks)
            {
                if (overlay.ControlNodesByBlockOrdinal[block.Ordinal] is not { } controlNode)
                {
                    continue;
                }

                var immediatePostDominator = overlay.ImmediatePostDominatorByBlockOrdinal[block.Ordinal];
                foreach (var successorOrdinal in GetSuccessorOrdinals(block))
                {
                    if (overlay.PostDominatorsByBlockOrdinal[block.Ordinal].Contains(successorOrdinal))
                    {
                        continue;
                    }

                    var runner = successorOrdinal;
                    var visited = new HashSet<int>();
                    while (visited.Add(runner) && runner != immediatePostDominator)
                    {
                        foreach (var dependentNode in overlay.NodesByBlockOrdinal[runner])
                        {
                            if (dependentNode.Id != controlNode.Id)
                            {
                                context.Graph.AddEdge(controlNode, dependentNode, RoslynCpgEdgeKind.ControlDependence);
                            }
                        }

                        var next = overlay.ImmediatePostDominatorByBlockOrdinal[runner];
                        if (next is null)
                        {
                            break;
                        }

                        runner = next.Value;
                    }
                }
            }
        }
    }

    private static IReadOnlyList<int> GetSuccessorOrdinals(Microsoft.CodeAnalysis.FlowAnalysis.BasicBlock block)
    {
        var successors = new SortedSet<int>();
        var fallThroughSuccessor = block.FallThroughSuccessor;
        if (fallThroughSuccessor?.Destination is not null)
        {
            successors.Add(fallThroughSuccessor.Destination.Ordinal);
        }

        var conditionalSuccessor = block.ConditionalSuccessor;
        if (conditionalSuccessor?.Destination is not null)
        {
            successors.Add(conditionalSuccessor.Destination.Ordinal);
        }

        return successors.ToArray();
    }
}

}
