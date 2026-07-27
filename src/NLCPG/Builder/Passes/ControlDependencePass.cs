using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{

    internal sealed class ControlDependencePass : INLCPGPass
    {
        internal static ControlDependencePass Instance { get; } = new();

        private ControlDependencePass()
        {
        }

        public string Name => nameof(ControlDependencePass);

        // 触发控制依赖 pass，把 dominance overlay 写回图边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunControlDependencePass(context);
        }
    }

}

namespace NLCPG.Builder
{

    public sealed partial class NLCPGBuilder
    {
        internal void RunControlDependencePass(NLCPGBuildContext context)
        {
            foreach (var overlay in _dominanceOverlays)
            {
                var blockCount = overlay.ControlFlowGraph.Blocks.Max(block => block.Ordinal) + 1;
                var visitedEpochByBlockOrdinal = new int[blockCount];
                var visitedEpoch = 0;
                foreach (var block in overlay.ControlFlowGraph.Blocks)
                {
                    // 没有控制节点的 basic block 不会产生控制依赖边。
                    if (overlay.ControlNodesByBlockOrdinal[block.Ordinal] is not { } controlNode)
                    {
                        continue;
                    }

                    var immediatePostDominator = overlay.ImmediatePostDominatorByBlockOrdinal[block.Ordinal];
                    var postDominators = overlay.PostDominatorsByBlockOrdinal[block.Ordinal];
                    var fallThroughSuccessorOrdinal = block.FallThroughSuccessor?.Destination?.Ordinal;
                    var conditionalSuccessorOrdinal = block.ConditionalSuccessor?.Destination?.Ordinal;
                    if (fallThroughSuccessorOrdinal is { } fallThroughOrdinal &&
                        conditionalSuccessorOrdinal is { } conditionalOrdinal)
                    {
                        if (fallThroughOrdinal <= conditionalOrdinal)
                        {
                            AddControlDependenceEdges(fallThroughOrdinal, controlNode, immediatePostDominator, postDominators, overlay, context, visitedEpochByBlockOrdinal, ref visitedEpoch);
                            if (fallThroughOrdinal != conditionalOrdinal)
                            {
                                AddControlDependenceEdges(conditionalOrdinal, controlNode, immediatePostDominator, postDominators, overlay, context, visitedEpochByBlockOrdinal, ref visitedEpoch);
                            }
                        }
                        else
                        {
                            AddControlDependenceEdges(conditionalOrdinal, controlNode, immediatePostDominator, postDominators, overlay, context, visitedEpochByBlockOrdinal, ref visitedEpoch);
                            AddControlDependenceEdges(fallThroughOrdinal, controlNode, immediatePostDominator, postDominators, overlay, context, visitedEpochByBlockOrdinal, ref visitedEpoch);
                        }
                    }
                    else if (fallThroughSuccessorOrdinal is { } singleFallThroughOrdinal)
                    {
                        AddControlDependenceEdges(singleFallThroughOrdinal, controlNode, immediatePostDominator, postDominators, overlay, context, visitedEpochByBlockOrdinal, ref visitedEpoch);
                    }
                    else if (conditionalSuccessorOrdinal is { } singleConditionalOrdinal)
                    {
                        AddControlDependenceEdges(singleConditionalOrdinal, controlNode, immediatePostDominator, postDominators, overlay, context, visitedEpochByBlockOrdinal, ref visitedEpoch);
                    }
                }
            }
        }

        private static void AddControlDependenceEdges(
            int successorOrdinal,
            NLCPGNode controlNode,
            int? immediatePostDominator,
            BlockBitSet postDominators,
            DominanceMethodOverlay overlay,
            NLCPGBuildContext context,
            int[] visitedEpochByBlockOrdinal,
            ref int visitedEpoch)
        {
            // 后支配后继不受当前条件控制，直接跳过。
            if (postDominators.Contains(successorOrdinal))
            {
                return;
            }

            // 以 epoch 标记本次链遍历，避免每个后继都分配 HashSet。
            if (visitedEpoch == int.MaxValue)
            {
                Array.Clear(visitedEpochByBlockOrdinal);
                visitedEpoch = 0;
            }

            visitedEpoch += 1;
            var runner = successorOrdinal;
            while (runner != immediatePostDominator &&
                   visitedEpochByBlockOrdinal[runner] != visitedEpoch)
            {
                visitedEpochByBlockOrdinal[runner] = visitedEpoch;
                foreach (var dependentNode in overlay.NodesByBlockOrdinal[runner])
                {
                    // 控制节点自身不需要连回自己。
                    if (!ReferenceEquals(dependentNode, controlNode))
                    {
                        context.Graph.AddEdge(controlNode, dependentNode, NLCPGEdgeKind.ControlDependence);
                    }
                }

                // 走不到下一个后支配点时结束，避免悬空块死循环。
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
