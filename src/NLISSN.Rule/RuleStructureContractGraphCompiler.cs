namespace NLISSN.Core.Pipeline;

/// <summary>
/// Declares one rule node using only marked-structure input and output contracts.
/// </summary>
public sealed record RuleStructureContractGraphNode(
  RuleNodeId NodeId,
  RuleKind Kind,
  RuleConsumesContract Consumes,
  RuleProducesContract Produces,
  bool IsEnabled = true);

/// <summary>
/// Describes one producer port that satisfies one consumer port.
/// </summary>
public sealed record RuleStructureContractEdge(
  RuleNodeId Producer,
  RuleNodeId Consumer,
  MarkedStructureSelector Selector);

/// <summary>
/// Holds the validated dependency graph materialized from rule contracts.
/// </summary>
public sealed class CompiledRuleStructureContractGraph
{
    private readonly IReadOnlyDictionary<RuleStructureContractConsumerKey, IReadOnlyList<RuleNodeId>>
      _producersByConsumerSelector;

    public CompiledRuleStructureContractGraph(
      IReadOnlyList<RuleStructureContractGraphNode> nodes,
      IReadOnlyList<RuleStructureContractEdge> edges,
      IReadOnlyDictionary<RuleNodeId, int> nodeIndexes)
    {
        Nodes = nodes;
        Edges = edges;
        NodeIndexes = nodeIndexes;
        _producersByConsumerSelector = edges
          .GroupBy(edge => new RuleStructureContractConsumerKey(edge.Consumer, edge.Selector))
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<RuleNodeId>)group
              .Select(edge => edge.Producer)
              .ToList());
    }

    public IReadOnlyList<RuleStructureContractGraphNode> Nodes { get; }

    public IReadOnlyList<RuleStructureContractEdge> Edges { get; }

    public IReadOnlyDictionary<RuleNodeId, int> NodeIndexes { get; }

    public IReadOnlyList<RuleNodeId> GetProducers(
      RuleNodeId consumer,
      MarkedStructureSelector selector)
    {
        return _producersByConsumerSelector.TryGetValue(
          new RuleStructureContractConsumerKey(consumer, selector),
          out var producers)
          ? producers
          : Array.Empty<RuleNodeId>();
    }

    private sealed record RuleStructureContractConsumerKey(
      RuleNodeId Consumer,
      MarkedStructureSelector Selector);
}

/// <summary>
/// Compiles explicit structural rule contracts into deterministic graph edges.
/// </summary>
public sealed class RuleStructureContractGraphCompiler
{
    public CompiledRuleStructureContractGraph Compile(
      IReadOnlyList<RuleStructureContractGraphNode> declaredNodes)
    {
        ArgumentNullException.ThrowIfNull(declaredNodes);

        var nodesById = new Dictionary<RuleNodeId, RuleStructureContractGraphNode>();
        foreach (var node in declaredNodes)
        {
            if (!nodesById.TryAdd(node.NodeId, node))
            {
                throw new InvalidOperationException(
                  $"Rule contract graph contains duplicate node '{node.NodeId.Value}'.");
            }
        }

        var declarationIndexes = declaredNodes
          .Select((node, index) => (node.NodeId, index))
          .ToDictionary(entry => entry.NodeId, entry => entry.index);
        var edges = new List<RuleStructureContractEdge>();
        foreach (var consumer in declaredNodes)
        {
            ValidateDistinctConsumes(consumer);
            foreach (var consumed in consumer.Consumes.Structures)
            {
                var producers = declaredNodes
                  .Where(producer => producer.Produces.Structures.Any(selector =>
                    RuleStructureContractMatcher.IsCompatible(selector, consumed.Selector)))
                  .ToList();
                ValidateCardinality(consumer, consumed, producers);
                foreach (var producer in producers)
                {
                    edges.Add(new RuleStructureContractEdge(
                      producer.NodeId,
                      consumer.NodeId,
                      consumed.Selector));
                }
            }
        }

        var orderedNodes = OrderNodes(declaredNodes, edges, declarationIndexes);
        return new CompiledRuleStructureContractGraph(orderedNodes, edges, declarationIndexes);
    }

    private static void ValidateDistinctConsumes(RuleStructureContractGraphNode consumer)
    {
        var duplicateSelector = consumer.Consumes.Structures
          .GroupBy(consumed => consumed.Selector)
          .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSelector is not null)
        {
            throw new InvalidOperationException(
              $"Rule node '{consumer.NodeId.Value}' consumes selector " +
              $"'{Format(duplicateSelector.Key)}' more than once.");
        }
    }

    private static void ValidateCardinality(
      RuleStructureContractGraphNode consumer,
      RuleConsumedStructure consumed,
      IReadOnlyList<RuleStructureContractGraphNode> producers)
    {
        if (consumed.Cardinality == RuleInputCardinality.All)
        {
            return;
        }

        if (producers.Count == 0 && consumed.Cardinality == RuleInputCardinality.Optional)
        {
            return;
        }

        if (producers.Count == 1)
        {
            return;
        }

        var cardinality = consumed.Cardinality == RuleInputCardinality.ExactlyOne
          ? "exactly one"
          : "at most one";
        var producerList = producers.Count == 0
          ? "none"
          : string.Join(", ", producers.Select(producer => producer.NodeId.Value));
        throw new InvalidOperationException(
          $"Rule node '{consumer.NodeId.Value}' requires {cardinality} producer for " +
          $"'{Format(consumed.Selector)}', but found{(producers.Count == 0 ? " " : ": ")}{producerList}.");
    }

    private static IReadOnlyList<RuleStructureContractGraphNode> OrderNodes(
      IReadOnlyList<RuleStructureContractGraphNode> declaredNodes,
      IReadOnlyList<RuleStructureContractEdge> edges,
      IReadOnlyDictionary<RuleNodeId, int> declarationIndexes)
    {
        var indegrees = declaredNodes.ToDictionary(node => node.NodeId, _ => 0);
        var downstream = declaredNodes.ToDictionary(node => node.NodeId, _ => new List<RuleNodeId>());
        foreach (var edge in edges.DistinctBy(edge => (edge.Producer, edge.Consumer)))
        {
            indegrees[edge.Consumer]++;
            downstream[edge.Producer].Add(edge.Consumer);
        }

        var ready = new PriorityQueue<RuleNodeId, int>();
        foreach (var node in declaredNodes)
        {
            if (indegrees[node.NodeId] == 0)
            {
                ready.Enqueue(node.NodeId, declarationIndexes[node.NodeId]);
            }
        }

        var ordered = new List<RuleStructureContractGraphNode>(declaredNodes.Count);
        var nodesById = declaredNodes.ToDictionary(node => node.NodeId);
        while (ready.TryDequeue(out var nodeId, out _))
        {
            ordered.Add(nodesById[nodeId]);
            foreach (var consumer in downstream[nodeId].OrderBy(id => declarationIndexes[id]))
            {
                indegrees[consumer]--;
                if (indegrees[consumer] == 0)
                {
                    ready.Enqueue(consumer, declarationIndexes[consumer]);
                }
            }
        }

        if (ordered.Count != declaredNodes.Count)
        {
            var cycleNodeIds = declaredNodes
              .Where(node => indegrees[node.NodeId] > 0)
              .Select(node => node.NodeId.Value);
            throw new InvalidOperationException(
              $"Rule contract graph contains a cycle: {string.Join(", ", cycleNodeIds)}.");
        }

        return ordered;
    }

    private static string Format(MarkedStructureSelector selector)
    {
        return $"({selector.StructureKind}, {selector.Role}, {selector.SemanticTag.Value})";
    }
}
