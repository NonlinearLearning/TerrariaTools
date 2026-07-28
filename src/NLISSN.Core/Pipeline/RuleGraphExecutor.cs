using NL.Concurrency;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed record RuleNodeResult(IReadOnlyDictionary<RuleOutputKind, IReadOnlyList<object>> Outputs)
{
    public static RuleNodeResult Empty { get; } = new(new Dictionary<RuleOutputKind, IReadOnlyList<object>>());

    public static RuleNodeResult From(RuleOutputKind outputKind, params object[] values)
    {
        return new RuleNodeResult(new Dictionary<RuleOutputKind, IReadOnlyList<object>>
        {
            [outputKind] = values
        });
    }

    public static RuleNodeResult FromProducedOutputs(IReadOnlyList<RuleOutputKind> outputKinds, IReadOnlyList<object> values)
    {
        return new RuleNodeResult(outputKinds.ToDictionary(
          outputKind => outputKind,
          outputKind => IsStageOutput(outputKind)
            ? values
            : (IReadOnlyList<object>)values.Where(value => GetTypedOutputKind(value) == outputKind).ToList()));
    }

    public IReadOnlyList<object> GetOutputs(RuleOutputKind outputKind)
    {
        return Outputs.TryGetValue(outputKind, out var values) ? values : Array.Empty<object>();
    }

    private static bool IsStageOutput(RuleOutputKind outputKind)
    {
        return outputKind is RuleOutputKind.SeedMark or RuleOutputKind.PropagatedMark or
            RuleOutputKind.LiftedMark or RuleOutputKind.DecisionUnit;
    }

    private static RuleOutputKind? GetTypedOutputKind(object value)
    {
        return value switch
        {
            MarkRecord mark => mark.OutputKind,
            PropagatedMarkRecord propagated => propagated.Mark.OutputKind,
            LiftedMarkRecord lifted => lifted.Mark.OutputKind,
            _ => null,
        };
    }
}

public sealed class RuleNodeInputs
{
    private readonly IReadOnlyDictionary<RuleNodeId, RuleNodeResult> _producerResults;

    public RuleNodeInputs(IReadOnlyDictionary<RuleNodeId, RuleNodeResult> producerResults)
    {
        _producerResults = producerResults;
    }

    public IReadOnlyList<object> GetOutputs(RuleNodeId producer, RuleOutputKind outputKind)
    {
        return _producerResults.TryGetValue(producer, out var result)
          ? result.GetOutputs(outputKind)
          : Array.Empty<object>();
    }

    public int OutputCount => _producerResults.Values.Sum(result => result.Outputs.Values.Sum(values => values.Count));
}

public sealed record RuleGraphExecutionNode(
  RuleGraphNode Node,
  Func<RuleNodeInputs, CancellationToken, Task<RuleNodeResult>> ExecuteAsync);

public sealed record RuleGraphExecutionResult(
  IReadOnlyList<RuleGraphExecutionNodeResult> Nodes,
  IReadOnlyList<RuleGraphNodeTelemetry>? Telemetry = null,
  RuleGraphExecutionMetrics? Metrics = null)
{
    public IReadOnlyList<object> GetOutputs(RuleNodeId nodeId, RuleOutputKind outputKind)
    {
        return Nodes.Single(node => node.NodeId == nodeId).Result.GetOutputs(outputKind);
    }
}

public sealed record RuleGraphExecutionNodeResult(RuleNodeId NodeId, RuleNodeResult Result);

public sealed record RuleGraphNodeTelemetry(RuleNodeId NodeId, int InputCount, int OutputCount, long ElapsedMilliseconds);

public sealed record RuleGraphExecutionMetrics(int PeakReadyNodeCount, int PeakConcurrentNodeCount);

public sealed class RuleGraphExecutor
{
    private readonly IConcurrencyPool _concurrencyPool;

    public RuleGraphExecutor(IConcurrencyPool? concurrencyPool = null)
    {
        _concurrencyPool = concurrencyPool ?? new BoundedConcurrencyPool();
    }

    public async Task<RuleGraphExecutionResult> ExecuteAsync(
      CompiledRuleGraph graph,
      IReadOnlyList<RuleGraphExecutionNode> executionNodes,
      int maxDegreeOfParallelism,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(executionNodes);

        var executors = executionNodes.ToDictionary(node => node.Node.NodeId);
        if (executors.Count != graph.Nodes.Count || graph.Nodes.Any(node => !executors.ContainsKey(node.NodeId)))
        {
            throw new InvalidOperationException("Execution nodes must exactly match the compiled rule graph.");
        }

        var workItems = graph.Nodes.Select(node => new DependencyWorkItem<RuleNodeId, NodeExecutionCompletion>(
          node.NodeId,
          node.Dependencies.Select(dependency => dependency.Producer).Distinct().ToList(),
          async (dependencyResults, token) =>
          {
              var inputs = BuildInputs(node, dependencyResults.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Result));
              var stopwatch = System.Diagnostics.Stopwatch.StartNew();
              var result = await executors[node.NodeId].ExecuteAsync(inputs, token);
              stopwatch.Stop();
              return new NodeExecutionCompletion(result, inputs.OutputCount, stopwatch.ElapsedMilliseconds);
          })).ToList();

        var execution = await _concurrencyPool.RunDependencyGraphAsync(
          workItems,
          maxDegreeOfParallelism,
          new GraphOrderComparer(graph.NodeIndexes),
          cancellationToken);

        return new RuleGraphExecutionResult(
          graph.Nodes.Select(node => new RuleGraphExecutionNodeResult(node.NodeId, execution.Results[node.NodeId].Result)).ToList(),
          graph.Nodes.Select(node =>
          {
              var completion = execution.Results[node.NodeId];
              return new RuleGraphNodeTelemetry(
                node.NodeId,
                completion.InputCount,
                completion.Result.Outputs.Values.Sum(values => values.Count),
                completion.ElapsedMilliseconds);
          }).ToList(),
          new RuleGraphExecutionMetrics(execution.PeakReadyWorkItemCount, execution.PeakConcurrentWorkItemCount));
    }

    private static RuleNodeInputs BuildInputs(
      RuleGraphNode node,
      IReadOnlyDictionary<RuleNodeId, RuleNodeResult> results)
    {
        return new RuleNodeInputs(node.Dependencies
          .Select(dependency => dependency.Producer)
          .Distinct()
          .ToDictionary(producer => producer, producer => results[producer]));
    }

    private sealed class GraphOrderComparer : IComparer<RuleNodeId>
    {
        private readonly IReadOnlyDictionary<RuleNodeId, int> _indexes;

        public GraphOrderComparer(IReadOnlyDictionary<RuleNodeId, int> indexes)
        {
            _indexes = indexes;
        }

        public int Compare(RuleNodeId? left, RuleNodeId? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            return _indexes[left].CompareTo(_indexes[right]);
        }
    }

    private sealed record NodeExecutionCompletion(RuleNodeResult Result, int InputCount, long ElapsedMilliseconds);
}
