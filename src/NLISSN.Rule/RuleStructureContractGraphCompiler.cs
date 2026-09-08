namespace NLISSN.Core.Pipeline;

/// <summary>
/// Selects whether propagation-internal edges participate in outer DAG cycle checks.
/// </summary>
public enum RuleStructureContractGraphMode
{
    Default = 0,
    PropagationFixedPointRegion = 1
}

/// <summary>
/// Declares one rule node using direct syntax-tag contracts.
/// </summary>
public sealed record RuleStructureContractGraphNode(
  RuleNodeId NodeId,
  RuleConsumesContract Consumes,
  RuleProducesContract Produces,
  RuleInputCardinality InputCardinality = RuleInputCardinality.All);

/// <summary>
/// Describes one producer output that satisfies one consumer input.
/// </summary>
public sealed record RuleStructureContractEdge(
  RuleNodeId Producer,
  RuleNodeId Consumer,
  RuleConsumedSyntax Input);

/// <summary>
/// Holds the validated dependency graph materialized from syntax-tag contracts.
/// </summary>
public sealed class CompiledRuleStructureContractGraph
{
    public CompiledRuleStructureContractGraph(IReadOnlyList<RuleStructureContractEdge> edges)
    {
        Edges = edges;
    }

    public IReadOnlyList<RuleStructureContractEdge> Edges { get; }

    public IReadOnlyList<RuleNodeId> GetProducers(
      RuleNodeId consumer,
      RuleConsumedSyntax input)
    {
        return Edges
          .Where(edge => edge.Consumer == consumer && edge.Input == input)
          .Select(edge => edge.Producer)
          .ToList();
    }
}

/// <summary>
/// Compiles explicit syntax-tag contracts into deterministic graph edges.
/// </summary>
public sealed class RuleStructureContractGraphCompiler
{
    public CompiledRuleStructureContractGraph Compile(
      IReadOnlyList<RuleStructureContractGraphNode> declaredNodes,
      RuleStructureContractGraphMode mode = RuleStructureContractGraphMode.Default)
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
            ValidateDistinctInputs(consumer);
            foreach (var input in consumer.Consumes.Inputs)
            {
                var sameTagOutputs = declaredNodes
                  .SelectMany(producer => producer.Produces.Outputs.Select(output => (producer, output)))
                  .Where(entry => RuleFactKindDescriptor.Matches(
                    entry.output.FactKind,
                    entry.output.SemanticTag,
                    input.FactKind,
                    input.SemanticTag))
                  .ToList();
                var incompatibleOutput = sameTagOutputs
                  .FirstOrDefault(entry => !RuleSyntaxContractMatcher.IsCompatible(entry.output, input));
                if (incompatibleOutput.output is not null)
                {
                    throw new InvalidOperationException(
                      $"Rule node '{consumer.NodeId.Value}' accepts syntax tag '{input.SemanticTag.Value}' " +
                      $"with syntax kinds '{string.Join(", ", input.SyntaxKinds)}', but producer " +
                      $"'{incompatibleOutput.producer.NodeId.Value}' declares incompatible syntax kinds " +
                      $"'{string.Join(", ", incompatibleOutput.output.SyntaxKinds)}'.");
                }

                var producers = sameTagOutputs
                  .Where(entry => RuleSyntaxContractMatcher.IsCompatible(entry.output, input))
                  .Select(entry => entry.producer)
                  .Distinct()
                  .ToList();
                ValidateInputCardinality(consumer, input, producers);
                foreach (var producer in producers)
                {
                    edges.Add(new RuleStructureContractEdge(
                      producer.NodeId,
                      consumer.NodeId,
                      input));
                }
            }
        }

        var acyclicityEdges = mode == RuleStructureContractGraphMode.PropagationFixedPointRegion
          ? edges.Where(edge => !IsPropagationInternalEdge(edge)).ToList()
          : edges;
        ValidateAcyclic(declaredNodes, acyclicityEdges, declarationIndexes);
        return new CompiledRuleStructureContractGraph(edges);
    }

    private static bool IsPropagationInternalEdge(RuleStructureContractEdge edge)
    {
        return edge.Producer.Kind == RuleKind.Propagate &&
          edge.Consumer.Kind == RuleKind.Propagate;
    }

    private static void ValidateDistinctInputs(RuleStructureContractGraphNode consumer)
    {
        var duplicateInput = consumer.Consumes.Inputs
          .GroupBy(input => input)
          .FirstOrDefault(group => group.Count() > 1);
        if (duplicateInput is not null)
        {
            throw new InvalidOperationException(
              $"Rule node '{consumer.NodeId.Value}' consumes syntax tag " +
              $"'{duplicateInput.Key.SemanticTag.Value}' more than once.");
        }
    }

    private static void ValidateInputCardinality(
      RuleStructureContractGraphNode consumer,
      RuleConsumedSyntax input,
      IReadOnlyList<RuleStructureContractGraphNode> producers)
    {
        if (consumer.InputCardinality == RuleInputCardinality.All)
        {
            return;
        }

        if (producers.Count == 1 ||
            (producers.Count == 0 && consumer.InputCardinality == RuleInputCardinality.Optional))
        {
            return;
        }

        var cardinality = consumer.InputCardinality == RuleInputCardinality.ExactlyOne
          ? "exactly one"
          : "at most one";
        var producerList = producers.Count == 0
          ? "none"
          : string.Join(", ", producers.Select(producer => producer.NodeId.Value));
        throw new InvalidOperationException(
          $"Rule node '{consumer.NodeId.Value}' requires {cardinality} producer for syntax tag " +
          $"'{input.SemanticTag.Value}' and syntax kinds '{string.Join(", ", input.SyntaxKinds)}', " +
          $"but found{(producers.Count == 0 ? " " : ": ")}{producerList}.");
    }

    private static void ValidateAcyclic(
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

        var visitedCount = 0;
        while (ready.TryDequeue(out var nodeId, out _))
        {
            visitedCount++;
            foreach (var consumer in downstream[nodeId].OrderBy(id => declarationIndexes[id]))
            {
                indegrees[consumer]--;
                if (indegrees[consumer] == 0)
                {
                    ready.Enqueue(consumer, declarationIndexes[consumer]);
                }
            }
        }

        if (visitedCount != declaredNodes.Count)
        {
            var cycleNodeIds = declaredNodes
              .Where(node => indegrees[node.NodeId] > 0)
              .Select(node => node.NodeId.Value);
            throw new InvalidOperationException(
              $"Rule contract graph contains a cycle: {string.Join(", ", cycleNodeIds)}.");
        }

    }
}
