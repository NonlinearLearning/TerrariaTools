using NL.Concurrency;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Performance;

namespace NLISSN.Core.Pipeline;

public enum RuleGraphNodeStatus
{
    Completed,
    Disabled
}

public sealed record RuleNodeResult(IReadOnlyList<object> Values)
{
    public IReadOnlyDictionary<RuleProducedSyntax, IReadOnlyList<object>> SyntaxOutputs { get; init; } =
      new Dictionary<RuleProducedSyntax, IReadOnlyList<object>>();

    public static RuleNodeResult Empty { get; } = new(Array.Empty<object>());

    public static RuleNodeResult FromValues(
      IReadOnlyList<object> values,
      RuleProducesContract produces)
    {
        ArgumentNullException.ThrowIfNull(produces);

        var result = new RuleNodeResult(values);
        if (produces.Outputs.Count > 0)
        {
            var syntaxOutputs = new Dictionary<RuleProducedSyntax, List<object>>();
            foreach (var value in values)
            {
                var mark = GetMarkedRecord(value);
                if (mark?.FactKind is null && mark?.SemanticTag is null)
                {
                    continue;
                }

                var output = RuleSyntaxContractValidator.RequireProducedMark(produces, mark);
                if (!syntaxOutputs.TryGetValue(output, out var outputValues))
                {
                    outputValues = new List<object>();
                    syntaxOutputs.Add(output, outputValues);
                }

                outputValues.Add(value);
            }

            result = result with
            {
                SyntaxOutputs = syntaxOutputs.ToDictionary(
                  entry => entry.Key,
                  entry => (IReadOnlyList<object>)entry.Value)
            };
        }

        return result;
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
        if (observedProduces.Outputs.Count == 0)
        {
            return result;
        }

        var syntaxOutputs = new Dictionary<RuleProducedSyntax, List<object>>();
        foreach (var value in values)
        {
            var mark = GetMarkedRecord(value);
            if (mark is null)
            {
                continue;
            }

            if (!RuleSyntaxContractValidator.TryGetObservedProducedMark(
                  observedProduces,
                  mark,
                  out var output) ||
                output is null)
            {
                continue;
            }

            if (!syntaxOutputs.TryGetValue(output, out var outputValues))
            {
                outputValues = new List<object>();
                syntaxOutputs.Add(output, outputValues);
            }

            outputValues.Add(value);
        }

        return result with
        {
            SyntaxOutputs = syntaxOutputs.ToDictionary(
              entry => entry.Key,
              entry => (IReadOnlyList<object>)entry.Value)
        };
    }

    public IReadOnlyList<object> GetOutputs(RuleConsumedSyntax input)
    {
        return SyntaxOutputs
          .Where(entry => RuleSyntaxContractMatcher.IsCompatible(entry.Key, input))
          .SelectMany(entry => entry.Value)
          .ToList();
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

    public IReadOnlyList<object> GetOutputs(RuleNodeId producer, RuleConsumedSyntax input)
    {
        return _producerResults.TryGetValue(producer, out var result)
          ? result.Result.GetOutputs(input)
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
  RuleGraphExecutionMetrics? Metrics = null);

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
    private readonly WorkScheduler? _injectedScheduler;

    public RuleGraphExecutor(WorkScheduler? scheduler = null)
    {
        _injectedScheduler = scheduler;
    }

    public async Task<RuleGraphExecutionResult> ExecuteAsync(
      CompiledRuleGraph graph,
      IReadOnlyList<RuleGraphExecutionNode> executionNodes,
      int maxDegreeOfParallelism,
      CancellationToken cancellationToken = default,
      IPerformanceEventSink? performanceEventSink = null,
      string? runId = null,
      string? itemId = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(executionNodes);

        var executors = executionNodes.ToDictionary(node => node.Node.NodeId);
        if (executors.Count != graph.Nodes.Count || graph.Nodes.Any(node => !executors.ContainsKey(node.NodeId)))
        {
            throw new InvalidOperationException("Execution nodes must exactly match the compiled rule graph.");
        }

        // 未注入内核时（独立调用方与测试）自建一个，用后即弃，
        // 避免把长期 worker 泄漏到进程生命周期之外。
        var scheduler = _injectedScheduler ?? new WorkScheduler(WorkSchedulerOptions.CreateDefault());
        var ownsScheduler = _injectedScheduler is null;

        try
        {
            return await ExecuteCoreAsync(
              graph,
              executors,
              scheduler,
              maxDegreeOfParallelism,
              cancellationToken,
              performanceEventSink,
              runId,
              itemId).ConfigureAwait(false);
        }
        finally
        {
            if (ownsScheduler)
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<RuleGraphExecutionResult> ExecuteCoreAsync(
      CompiledRuleGraph graph,
      IReadOnlyDictionary<RuleNodeId, RuleGraphExecutionNode> executors,
      WorkScheduler scheduler,
      int maxDegreeOfParallelism,
      CancellationToken cancellationToken,
      IPerformanceEventSink? performanceEventSink,
      string? runId,
      string? itemId)
    {
        var workItems = graph.Nodes.Select(node => new WorkItem<NodeExecutionCompletion>
        {
            StableOrder = graph.NodeIndexes[node.NodeId],
            Dependencies = node.Dependencies
              .Select(dependency => (long)graph.NodeIndexes[dependency.Producer])
              .Distinct()
              .ToArray(),
            Priority = WorkPriority.LatencySensitive,
            ExecuteAsync = async (dependencyResults, token) =>
            {
                var executor = executors[node.NodeId];
                var inputs = BuildInputs(node, dependencyResults, graph.NodeIndexes);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await executor.ExecuteAsync(inputs, token);
                stopwatch.Stop();
                return new NodeExecutionCompletion(
                  result,
                  inputs.OutputCount,
                  stopwatch.ElapsedMilliseconds,
                  executor.Status);
            },
        }).ToList();

        var execution = await scheduler.RunWithMetricsAsync(
          new WorkSubmission<NodeExecutionCompletion>
          {
              Items = workItems,
              Category = WorkCategories.RuleGroup,
              MaxConcurrency = maxDegreeOfParallelism,
          },
          cancellationToken);

        // 内核按 StableOrder 升序归并，而 StableOrder 即 graph.NodeIndexes，
        // 故可直接按下标还原，无需再查 RuleNodeId 字典。
        var completionByIndex = execution.Results;

        return new RuleGraphExecutionResult(
          graph.Nodes.Select(node =>
            new RuleGraphExecutionNodeResult(
              node.NodeId,
              completionByIndex[graph.NodeIndexes[node.NodeId]].Result,
              completionByIndex[graph.NodeIndexes[node.NodeId]].Status)).ToList(),
          graph.Nodes.Select(node =>
          {
              var completion = completionByIndex[graph.NodeIndexes[node.NodeId]];
              performanceEventSink.TryRecord(new PerformanceEvent(
                runId ?? "unassigned",
                PerformanceStageId.ForRule(node.NodeId.Kind),
                itemId,
                completion.ElapsedMilliseconds,
                null,
                completion.Status == RuleGraphNodeStatus.Disabled
                  ? PerformanceStatus.Skipped
                  : PerformanceStatus.Completed,
                new Dictionary<string, long>
                {
                  ["inputCount"] = completion.InputCount,
                  ["outputCount"] = completion.Result.Values.Count
                }));
              return new RuleGraphNodeTelemetry(
                node.NodeId,
                completion.InputCount,
                completion.Result.Values.Count,
                completion.ElapsedMilliseconds,
                completion.Status);
          }).ToList(),
          new RuleGraphExecutionMetrics(execution.PeakReadyCount, execution.PeakActiveCount));
    }

    private static RuleNodeInputs BuildInputs(
      RuleGraphNode node,
      IReadOnlyDictionary<long, NodeExecutionCompletion> results,
      IReadOnlyDictionary<RuleNodeId, int> nodeIndexes)
    {
        // 内核以 StableOrder（即 nodeIndexes）为键传回前置结果，
        // 这里反查回 RuleNodeId 供规则消费，键空间不泄漏给规则实现。
        return new RuleNodeInputs(node.Dependencies
            .Select(dependency => dependency.Producer)
            .Distinct()
            .ToDictionary(
              producer => producer,
              producer =>
              {
                  var completion = results[nodeIndexes[producer]];
                  return new RuleGraphExecutionNodeResult(
                    producer,
                    completion.Result,
                    completion.Status);
              }));
    }

    private sealed record NodeExecutionCompletion(
      RuleNodeResult Result,
      int InputCount,
      long ElapsedMilliseconds,
      RuleGraphNodeStatus Status);
}
