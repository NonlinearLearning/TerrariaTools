using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Collections;
using System.Numerics;

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
    private sealed class BlockBitSet : IReadOnlySet<int>
    {
        private readonly int _capacity;
        private readonly ulong[] _words;

        internal BlockBitSet(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            _capacity = capacity;
            _words = new ulong[(capacity + 63) / 64];
        }

        private BlockBitSet(int capacity, ulong[] words)
        {
            _capacity = capacity;
            _words = words;
        }

        public int Count
        {
            get
            {
                var count = 0;
                foreach (var word in _words)
                {
                    count += BitOperations.PopCount(word);
                }

                return count;
            }
        }

        internal BlockBitSet Clone()
        {
            return new BlockBitSet(_capacity, (ulong[])_words.Clone());
        }

        internal void Set(int ordinal)
        {
            ValidateOrdinal(ordinal);
            _words[ordinal / 64] |= 1UL << (ordinal % 64);
        }

        internal void AndWith(BlockBitSet other)
        {
            ValidateCompatible(other);
            for (var index = 0; index < _words.Length; index += 1)
            {
                _words[index] &= other._words[index];
            }
        }

        internal bool BitwiseEquals(BlockBitSet other)
        {
            ValidateCompatible(other);
            for (var index = 0; index < _words.Length; index += 1)
            {
                if (_words[index] != other._words[index])
                {
                    return false;
                }
            }

            return true;
        }

        public bool Contains(int item)
        {
            if (item < 0 || item >= _capacity)
            {
                return false;
            }

            return (_words[item / 64] & (1UL << (item % 64))) != 0;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (var wordIndex = 0; wordIndex < _words.Length; wordIndex += 1)
            {
                var remaining = _words[wordIndex];
                while (remaining != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(remaining);
                    var ordinal = (wordIndex * 64) + bitIndex;
                    if (ordinal < _capacity)
                    {
                        yield return ordinal;
                    }

                    remaining &= remaining - 1;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool IsProperSubsetOf(IEnumerable<int> other)
        {
            return IsSubsetOf(other) && !SetEquals(other);
        }

        public bool IsProperSupersetOf(IEnumerable<int> other)
        {
            return IsSupersetOf(other) && !SetEquals(other);
        }

        public bool IsSubsetOf(IEnumerable<int> other)
        {
            if (other is BlockBitSet otherBitSet)
            {
                ValidateCompatible(otherBitSet);
                for (var index = 0; index < _words.Length; index += 1)
                {
                    if ((_words[index] & ~otherBitSet._words[index]) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            var otherSet = other.ToHashSet();
            return this.All(otherSet.Contains);
        }

        public bool IsSupersetOf(IEnumerable<int> other)
        {
            if (other is BlockBitSet otherBitSet)
            {
                ValidateCompatible(otherBitSet);
                for (var index = 0; index < _words.Length; index += 1)
                {
                    if ((otherBitSet._words[index] & ~_words[index]) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var ordinal in other)
            {
                if (!Contains(ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public bool Overlaps(IEnumerable<int> other)
        {
            if (other is BlockBitSet otherBitSet)
            {
                ValidateCompatible(otherBitSet);
                for (var index = 0; index < _words.Length; index += 1)
                {
                    if ((_words[index] & otherBitSet._words[index]) != 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            return other.Any(Contains);
        }

        public bool SetEquals(IEnumerable<int> other)
        {
            if (other is BlockBitSet otherBitSet)
            {
                return BitwiseEquals(otherBitSet);
            }

            var otherSet = other.ToHashSet();
            return Count == otherSet.Count && this.All(otherSet.Contains);
        }

        private void ValidateCompatible(BlockBitSet other)
        {
            if (_capacity != other._capacity)
            {
                throw new InvalidOperationException("BlockBitSet capacity mismatch.");
            }
        }

        private void ValidateOrdinal(int ordinal)
        {
            if (ordinal < 0 || ordinal >= _capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            }
        }
    }

    private readonly List<DominanceMethodOverlay> _dominanceOverlays = new();

    private sealed record DominanceMethodOverlay(
        ControlFlowGraph ControlFlowGraph,
        IReadOnlyDictionary<int, BlockBitSet> DominatorsByBlockOrdinal,
        IReadOnlyDictionary<int, BlockBitSet> PostDominatorsByBlockOrdinal,
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
            var blockBitSetCapacity = controlFlowGraph.Blocks.Max(block => block.Ordinal) + 1;
            var successorsByBlockOrdinal = BuildSuccessorsByBlockOrdinal(controlFlowGraph, blockBitSetCapacity);
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

    private Dictionary<int, IReadOnlyList<RoslynCpgNode>> MapNodesByBlockOrdinal(ControlFlowGraph controlFlowGraph, IMethodSymbol methodSymbol, RoslynCpgGraph graph)
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

    private void AddMappedOperationNode(IOperation operation, ISet<RoslynCpgNode> nodes, RoslynCpgGraph graph)
    {
        nodes.Add(GetOrCreateOperationNode(operation, graph));
    }

    private static Dictionary<int, RoslynCpgNode?> MapControlNodesByBlockOrdinal(ControlFlowGraph controlFlowGraph, IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> nodesByBlockOrdinal)
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

    private static Dictionary<int, BlockBitSet> BuildSuccessorsByBlockOrdinal(ControlFlowGraph controlFlowGraph, int blockBitSetCapacity)
    {
        var successorsByBlockOrdinal = controlFlowGraph.Blocks.ToDictionary(
            block => block.Ordinal,
            _ => new BlockBitSet(blockBitSetCapacity));
        foreach (var block in controlFlowGraph.Blocks)
        {
            var successors = successorsByBlockOrdinal[block.Ordinal];
            AddDestination(block.FallThroughSuccessor, successors);
            AddDestination(block.ConditionalSuccessor, successors);
        }

        return successorsByBlockOrdinal;
    }

    private static void AddDestination(ControlFlowBranch? branch, BlockBitSet successors)
    {
        if (branch?.Destination is not null)
        {
            successors.Set(branch.Destination.Ordinal);
        }
    }

    private static Dictionary<int, BlockBitSet> ReverseNeighbors(IReadOnlyDictionary<int, BlockBitSet> successorsByBlockOrdinal)
    {
        var blockBitSetCapacity = successorsByBlockOrdinal.Keys.Max() + 1;
        var predecessors = successorsByBlockOrdinal.Keys.ToDictionary(
            ordinal => ordinal,
            _ => new BlockBitSet(blockBitSetCapacity));
        foreach (var (sourceOrdinal, successors) in successorsByBlockOrdinal)
        {
            foreach (var targetOrdinal in successors)
            {
                predecessors[targetOrdinal].Set(sourceOrdinal);
            }
        }

        return predecessors;
    }

    private static IReadOnlyDictionary<int, BlockBitSet> CalculateDominators(IReadOnlyDictionary<int, BlockBitSet> successorsByBlockOrdinal, IReadOnlyDictionary<int, BlockBitSet> predecessorsByBlockOrdinal, int rootOrdinal)
    {
        var blockBitSetCapacity = successorsByBlockOrdinal.Keys.Max() + 1;
        var reachable = CalculateReversePostOrder(successorsByBlockOrdinal, rootOrdinal);
        var reachableSet = new BlockBitSet(blockBitSetCapacity);
        foreach (var ordinal in reachable)
        {
            reachableSet.Set(ordinal);
        }

        var dominators = successorsByBlockOrdinal.Keys.ToDictionary(
            ordinal => ordinal,
            ordinal => ordinal == rootOrdinal
                ? CreateSingletonBitSet(blockBitSetCapacity, ordinal)
                : reachableSet.Contains(ordinal)
                    ? reachableSet.Clone()
                    : CreateSingletonBitSet(blockBitSetCapacity, ordinal));

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

                var intersection = dominators[reachablePredecessors[0]].Clone();
                foreach (var predecessor in reachablePredecessors.Skip(1))
                {
                    intersection.AndWith(dominators[predecessor]);
                }

                intersection.Set(ordinal);
                if (!intersection.BitwiseEquals(dominators[ordinal]))
                {
                    dominators[ordinal] = intersection;
                    changed = true;
                }
            }
        }

        return dominators;
    }

    private static IReadOnlyList<int> CalculateReversePostOrder(IReadOnlyDictionary<int, BlockBitSet> successorsByBlockOrdinal, int rootOrdinal)
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

            foreach (var successor in successorsByBlockOrdinal[ordinal])
            {
                Visit(successor);
            }

            postOrder.Add(ordinal);
        }
    }

    private static IReadOnlyDictionary<int, int?> CalculateImmediatePostDominators(IReadOnlyDictionary<int, BlockBitSet> postDominatorsByBlockOrdinal)
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

    private static void AddOverlayEdges(IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> nodesByBlockOrdinal, IReadOnlyDictionary<int, BlockBitSet> relationsByTargetBlockOrdinal, RoslynCpgEdgeKind edgeKind, RoslynCpgGraph graph)
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

    private static void AddPostDominanceEdges(IReadOnlyDictionary<int, IReadOnlyList<RoslynCpgNode>> nodesByBlockOrdinal, IReadOnlyDictionary<int, BlockBitSet> postDominatorsByBlockOrdinal, RoslynCpgGraph graph)
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

    private static BlockBitSet CreateSingletonBitSet(int blockBitSetCapacity, int ordinal)
    {
        var singleton = new BlockBitSet(blockBitSetCapacity);
        singleton.Set(ordinal);
        return singleton;
    }
}

}
