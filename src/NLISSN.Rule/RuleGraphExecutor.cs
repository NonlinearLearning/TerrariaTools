using NL.Concurrency;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Pipeline;

public enum RuleGraphNodeStatus
{
    Completed,
    Disabled,
    Cancelled
}

public sealed record RuleNodeResult(IReadOnlyList<object> Values)
{
    public IReadOnlyDictionary<MarkedStructureSelector, IReadOnlyList<object>> StructureOutputs { get; init; } =
      new Dictionary<MarkedStructureSelector, IReadOnlyList<object>>();

    public static RuleNodeResult Empty { get; } = new(Array.Empty<object>());

    public static RuleNodeResult FromValues(
      IReadOnlyList<object> values,
      RuleProducesContract produces)
    {
        ArgumentNullException.ThrowIfNull(produces);

        var result = new RuleNodeResult(values);
        if (produces.Structures.Count == 0)
        {
            return result;
        }

        var structureOutputs = new Dictionary<MarkedStructureSelector, List<object>>();
        foreach (var value in values)
        {
            var mark = GetMarkedRecord(value);
            if (mark is null)
            {
                continue;
            }

            if (mark.SemanticTag is null)
            {
                continue;
            }

            var selector = RuleStructureContractValidator.RequireProducedMark(produces, mark);
            if (!structureOutputs.TryGetValue(selector, out var selectorValues))
            {
                selectorValues = new List<object>();
                structureOutputs.Add(selector, selectorValues);
            }

            selectorValues.Add(value);
        }

        return result with
        {
            StructureOutputs = structureOutputs.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<object>)entry.Value)
        };
    }

    /// <summary>
    /// Indexes only the structural ports observed in values that originate outside this graph.
    /// </summary>
    public static RuleNodeResult FromObservedValues(
      IReadOnlyList<object> values,
      RuleProducesContract observedProduces)
    {
        ArgumentNullException.ThrowIfNull(observedProduces);

        var result = new RuleNodeResult(values);
        if (observedProduces.Structures.Count == 0)
        {
            return result;
        }

        var structureOutputs = new Dictionary<MarkedStructureSelector, List<object>>();
        foreach (var value in values)
        {
            var mark = GetMarkedRecord(value);
            if (mark is null)
            {
                continue;
            }

            if (!RuleStructureContractValidator.TryGetObservedProducedMark(
                  observedProduces,
                  mark,
                  out var selector) ||
                selector is null)
            {
                continue;
            }

            if (!structureOutputs.TryGetValue(selector, out var selectorValues))
            {
                selectorValues = new List<object>();
                structureOutputs.Add(selector, selectorValues);
            }

            selectorValues.Add(value);
        }

        return result with
        {
            StructureOutputs = structureOutputs.ToDictionary(
              entry => entry.Key,
              entry => (IReadOnlyList<object>)entry.Value)
        };
    }

    public IReadOnlyList<object> GetOutputs(MarkedStructureSelector selector)
    {
        return StructureOutputs.TryGetValue(selector, out var values) ? values : Array.Empty<object>();
    }

    private static MarkRecord? GetMarkedRecord(object value)
    {
        return value switch
        {
            MarkRecord mark => mark,
            PropagatedMarkRecord propagated => propagated.Mark,
            LiftedMarkRecord lifted => lifted.Mark,
            _ => null
        };
    }
}

public sealed class RuleNodeInputs
{
    private readonly IReadOnlyDictionary<RuleNodeId, RuleGraphExecutionNodeResult> _producerResults;

    public RuleNodeInputs(IReadOnlyDictionary<RuleNodeId, RuleGraphExecutionNodeResult> producerResults)
    {
        _producerResults = producerResults;
    }

    public IReadOnlyList<object> GetValues(RuleNodeId producer)
    {
        return _producerResults.TryGetValue(producer, out var result)
          ? result.Result.Values
          : Array.Empty<object>();
    }

    public IReadOnlyList<object> GetOutputs(RuleNodeId producer, MarkedStructureSelector selector)
    {
        return _producerResults.TryGetValue(producer, out var result)
          ? result.Result.GetOutputs(selector)
          : Array.Empty<object>();
    }

    public RuleGraphNodeStatus GetStatus(RuleNodeId producer)
    {
        if (_producerResults.TryGetValue(producer, out var result))
        {
            return result.Status;
        }

        throw new InvalidOperationException($"Producer '{producer.Value}' is not available to this rule node.");
    }

    public int OutputCount => _producerResults.Values.Sum(result => result.Result.Values.Count);
}

public sealed record RuleGraphExecutionNode(
  RuleGraphNode Node,
  Func<RuleNodeInputs, CancellationToken, Task<RuleNodeResult>> ExecuteAsync,
  RuleGraphNodeStatus Status = RuleGraphNodeStatus.Completed);

public sealed record RuleGraphExecutionResult(
  IReadOnlyList<RuleGraphExecutionNodeResult> Nodes,
  IReadOnlyList<RuleGraphNodeTelemetry>? Telemetry = null,
  RuleGraphExecutionMetrics? Metrics = null)
{
    public IReadOnlyList<object> GetValues(RuleNodeId nodeId)
    {
        return Nodes.Single(node => node.NodeId == nodeId).Result.Values;
    }
}

public sealed record RuleGraphExecutionNodeResult(
  RuleNodeId NodeId,
  RuleNodeResult Result,
  RuleGraphNodeStatus Status = RuleGraphNodeStatus.Completed);

public sealed record RuleGraphNodeTelemetry(
  RuleNodeId NodeId,
  int InputCount,
  int OutputCount,
  long ElapsedMilliseconds,
  RuleGraphNodeStatus Status = RuleGraphNodeStatus.Completed);

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
              var executor = executors[node.NodeId];
              var inputs = BuildInputs(node, dependencyResults);
              var stopwatch = System.Diagnostics.Stopwatch.StartNew();
              var result = await executor.ExecuteAsync(inputs, token);
              stopwatch.Stop();
              return new NodeExecutionCompletion(
                result,
                inputs.OutputCount,
                stopwatch.ElapsedMilliseconds,
                executor.Status);
          })).ToList();

        var execution = await _concurrencyPool.RunDependencyGraphAsync(
          workItems,
          maxDegreeOfParallelism,
          new GraphOrderComparer(graph.NodeIndexes),
          cancellationToken);

        return new RuleGraphExecutionResult(
          graph.Nodes.Select(node =>
            new RuleGraphExecutionNodeResult(
              node.NodeId,
              execution.Results[node.NodeId].Result,
              execution.Results[node.NodeId].Status)).ToList(),
          graph.Nodes.Select(node =>
          {
              var completion = execution.Results[node.NodeId];
              return new RuleGraphNodeTelemetry(
                node.NodeId,
                completion.InputCount,
                completion.Result.Values.Count,
                completion.ElapsedMilliseconds,
                completion.Status);
          }).ToList(),
          new RuleGraphExecutionMetrics(execution.PeakReadyWorkItemCount, execution.PeakConcurrentWorkItemCount));
    }

    private static RuleNodeInputs BuildInputs(
      RuleGraphNode node,
      IReadOnlyDictionary<RuleNodeId, NodeExecutionCompletion> results)
    {
        return new RuleNodeInputs(node.Dependencies
            .Select(dependency => dependency.Producer)
            .Distinct()
            .ToDictionary(
              producer => producer,
              producer => new RuleGraphExecutionNodeResult(
                producer,
                results[producer].Result,
                results[producer].Status)));
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

    private sealed record NodeExecutionCompletion(
      RuleNodeResult Result,
      int InputCount,
      long ElapsedMilliseconds,
      RuleGraphNodeStatus Status);
}
