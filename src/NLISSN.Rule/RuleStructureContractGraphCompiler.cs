namespace NLISSN.Core.Pipeline;

/// <summary>
/// Declares one rule node using direct syntax-tag contracts.
/// </summary>
public sealed record RuleStructureContractGraphNode(
  RuleNodeId NodeId,
  RuleKind Kind,
  RuleConsumesContract Consumes,
  RuleProducesContract Produces,
  bool IsEnabled = true,
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
  public CompiledRuleStructureContractGraph(
    IReadOnlyList<RuleStructureContractGraphNode> nodes,
    IReadOnlyList<RuleStructureContractEdge> edges,
    IReadOnlyDictionary<RuleNodeId, int> nodeIndexes)
  {
    Nodes = nodes;
    Edges = edges;
    NodeIndexes = nodeIndexes;
  }

  public IReadOnlyList<RuleStructureContractGraphNode> Nodes { get; }

  public IReadOnlyList<RuleStructureContractEdge> Edges { get; }

  public IReadOnlyDictionary<RuleNodeId, int> NodeIndexes { get; }

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
      ValidateDistinctInputs(consumer);
      foreach (var input in consumer.Consumes.Inputs)
      {
        var sameTagOutputs = declaredNodes
          .SelectMany(producer => producer.Produces.Outputs.Select(output => (producer, output)))
          .Where(entry => entry.output.SemanticTag == input.SemanticTag)
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

    var orderedNodes = OrderNodes(declaredNodes, edges, declarationIndexes);
    return new CompiledRuleStructureContractGraph(orderedNodes, edges, declarationIndexes);
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
}
