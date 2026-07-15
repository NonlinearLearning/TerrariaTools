using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Collections.Immutable;
using System.Diagnostics;

namespace MinimalRoslynCpg.Builder.Passes
{
internal sealed class DataFlowPass : IRoslynCpgPass
{
  internal static DataFlowPass Instance { get; } = new();

  private DataFlowPass()
  {
  }

  public string Name => nameof(DataFlowPass);

  public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
  {
    builder.RunDataFlowPass(context);
  }
}
}

namespace MinimalRoslynCpg.Builder
{
public sealed partial class RoslynCpgBuilder
{
    private sealed class DataFlowPassMetrics
    {
        public long EnumerateMethodBlocksElapsedMilliseconds { get; set; }
        public long EnumerateOrderedOperationsElapsedMilliseconds { get; set; }
        public long CfgSensitiveElapsedMilliseconds { get; set; }
        public long ValueSourceEdgeElapsedMilliseconds { get; set; }
        public long ReturnFlowEdgeElapsedMilliseconds { get; set; }
        public long TerminalFlowEdgeElapsedMilliseconds { get; set; }
        public long CallArgumentAndReturnElapsedMilliseconds { get; set; }
        public long BuildFlowNeighborsElapsedMilliseconds { get; set; }
        public long FixpointElapsedMilliseconds { get; set; }
        public long ReachingDefinitionEdgeElapsedMilliseconds { get; set; }
        public long PrepareFlowNodesElapsedMilliseconds { get; set; }
        public long CollectUsedFactsElapsedMilliseconds { get; set; }
        public long CreateDefinitionFactsElapsedMilliseconds { get; set; }
        public long InitializeCfgSensitiveStateElapsedMilliseconds { get; set; }
        public long CfgSensitiveCandidateGenerationElapsedMilliseconds { get; set; }
        public long CfgSensitiveCandidateCommitElapsedMilliseconds { get; set; }
        public int MethodBlockCount { get; set; }
        public int OrderedOperationCount { get; set; }
        public int FlowNodeCount { get; set; }
        public int UsedFactCount { get; set; }
        public int DefinitionFactCount { get; set; }
        public int UsedFactPartitionCount { get; set; }
        public int UsedFactPartitionMaxDegreeOfParallelism { get; set; }
        public int CfgSensitivePartitionCount { get; set; }
        public int CfgSensitivePartitionMaxDegreeOfParallelism { get; set; }
        public int PeakBufferedCandidateBatchCount { get; set; }
        public int CandidateEdgeCount { get; set; }
        public int FrozenOperationNodeCount { get; set; }
        public int MethodOperationNodeProjectionCount { get; set; }
        public int UsedFactRecordCount { get; set; }
        public int SkippedMethodCount { get; set; }
        public List<RoslynCpgMethodDataFlowTelemetry> MethodTelemetry { get; } = new();
        public long PrepareFlowNodesElapsedTicks { get; set; }
        public long CollectUsedFactsElapsedTicks { get; set; }
    }

    private sealed record UsedFactRecord(
        IReadOnlyList<DefinitionFact> DirectFacts,
        IReadOnlyList<UsedFactRecord> ChildRecords,
        int FactCount)
    {
        public IEnumerable<DefinitionFact> EnumerateFacts()
        {
            foreach (var directFact in DirectFacts)
            {
                yield return directFact;
            }

            foreach (var childRecord in ChildRecords)
            {
                foreach (var fact in childRecord.EnumerateFacts())
                {
                    yield return fact;
                }
            }
        }
    }

    private sealed record UsedFactPartition(
        int Order,
        IReadOnlyList<IOperation> OrderedOperations,
        IReadOnlyDictionary<IOperation, UsedFactRecord> UsedFactsByOperation,
        long EnumerateOrderedOperationsMilliseconds,
        long CollectUsedFactsMilliseconds,
        long CollectUsedFactsTicks,
        int UsedFactCount);

    private sealed record MethodDataFlowPlan(
      int Order,
      string MethodFullName,
      ImmutableArray<IOperation> OrderedOperations,
      ImmutableArray<RoslynCpgNode> OperationNodes,
      ImmutableDictionary<IOperation, UsedFactRecord> UsedFactsByOperation,
      ImmutableDictionary<string, DefinitionFact> ParameterDefinitionFacts,
      ImmutableDictionary<string, RoslynCpgNode> NodesById,
      ImmutableDictionary<IOperation, string> OperationNodeIds,
      string? ReturnNodeId,
      string? ExitNodeId,
      ImmutableDictionary<string, ImmutableArray<string>> Predecessors,
      ImmutableDictionary<string, ImmutableArray<string>> Successors);

    private sealed record DataFlowEdgeCandidate(string SourceNodeId, string TargetNodeId);

    private sealed record CfgSensitivePartition(
      int Order,
      IReadOnlyList<DataFlowEdgeCandidate> Edges,
      long ElapsedMilliseconds,
      long CreateDefinitionFactsMilliseconds,
      long InitializeStateMilliseconds,
      long FixpointMilliseconds,
      long ReachingDefinitionEdgeMilliseconds,
      long ValueSourceEdgeMilliseconds,
      long ReturnFlowEdgeMilliseconds,
      long TerminalFlowEdgeMilliseconds,
      int FlowNodeCount,
      int DefinitionFactCount,
      int FixpointIterations,
      int UnreachableNodeCount,
      int GeneratedCandidateCount,
      RoslynCpgDataFlowOverflowReason OverflowReason);

    internal void RunDataFlowPass(RoslynCpgBuildContext context)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var metrics = new DataFlowPassMetrics();
        AddReachingDefinitionDataFlow(context.Graph, metrics);
        totalStopwatch.Stop();
        _dataFlowPassTelemetry = new RoslynCpgDataFlowPassTelemetry(
            totalStopwatch.ElapsedMilliseconds,
            metrics.EnumerateMethodBlocksElapsedMilliseconds,
            metrics.EnumerateOrderedOperationsElapsedMilliseconds,
            metrics.CfgSensitiveElapsedMilliseconds,
            metrics.ValueSourceEdgeElapsedMilliseconds,
            metrics.ReturnFlowEdgeElapsedMilliseconds,
            metrics.TerminalFlowEdgeElapsedMilliseconds,
            metrics.CallArgumentAndReturnElapsedMilliseconds,
            metrics.BuildFlowNeighborsElapsedMilliseconds,
            metrics.FixpointElapsedMilliseconds,
            metrics.ReachingDefinitionEdgeElapsedMilliseconds,
            metrics.MethodBlockCount,
            metrics.OrderedOperationCount,
            metrics.PrepareFlowNodesElapsedMilliseconds,
            metrics.CollectUsedFactsElapsedMilliseconds,
            metrics.CreateDefinitionFactsElapsedMilliseconds,
            metrics.InitializeCfgSensitiveStateElapsedMilliseconds,
            metrics.FlowNodeCount,
            metrics.UsedFactCount,
            metrics.DefinitionFactCount,
            metrics.UsedFactPartitionCount,
            metrics.UsedFactPartitionMaxDegreeOfParallelism,
            metrics.CfgSensitivePartitionCount,
            metrics.CfgSensitivePartitionMaxDegreeOfParallelism,
            metrics.CfgSensitiveCandidateGenerationElapsedMilliseconds,
            metrics.CfgSensitiveCandidateCommitElapsedMilliseconds,
            metrics.PeakBufferedCandidateBatchCount,
            metrics.CandidateEdgeCount,
            metrics.FrozenOperationNodeCount,
            metrics.MethodOperationNodeProjectionCount,
            metrics.UsedFactRecordCount,
            metrics.PrepareFlowNodesElapsedTicks,
            metrics.CollectUsedFactsElapsedTicks,
            metrics.SkippedMethodCount,
            metrics.MethodTelemetry
              .OrderBy(method => method.MethodFullName, StringComparer.Ordinal)
              .ToArray());
    }

    private void AddReachingDefinitionDataFlow(RoslynCpgGraph graph, DataFlowPassMetrics metrics)
    {
        var enumerateMethodBlocksStopwatch = Stopwatch.StartNew();
        var methodBlocks = _operationNodes.Keys.OfType<IBlockOperation>().Where(IsMethodRootBlock).ToList();
        enumerateMethodBlocksStopwatch.Stop();
        metrics.EnumerateMethodBlocksElapsedMilliseconds = enumerateMethodBlocksStopwatch.ElapsedMilliseconds;
        metrics.MethodBlockCount = methodBlocks.Count;

        var prepareFlowNodesStopwatch = Stopwatch.StartNew();
        var operationIndex = FreezeDataFlowOperationIndex();
        var usedFactPartitions = RunUsedFactPartitionsAsync(methodBlocks)
          .GetAwaiter()
          .GetResult();
        metrics.UsedFactPartitionCount = usedFactPartitions.Count;
        metrics.UsedFactPartitionMaxDegreeOfParallelism = _options.EffectiveMaxDegreeOfParallelism;
        var cfgSensitivePlans = BuildCfgSensitivePartitionPlans(
          usedFactPartitions,
          methodBlocks,
          operationIndex,
          graph,
          metrics);
        prepareFlowNodesStopwatch.Stop();
        metrics.PrepareFlowNodesElapsedMilliseconds = prepareFlowNodesStopwatch.ElapsedMilliseconds;
        metrics.PrepareFlowNodesElapsedTicks = prepareFlowNodesStopwatch.ElapsedTicks;
        metrics.FrozenOperationNodeCount = operationIndex.NodeIdsByOperation.Count;
        metrics.PeakBufferedCandidateBatchCount = RunCfgSensitivePartitionsInOrder(
          cfgSensitivePlans,
          graph,
          metrics);
        metrics.CfgSensitivePartitionCount = cfgSensitivePlans.Count;
        metrics.CfgSensitivePartitionMaxDegreeOfParallelism = _options.EffectiveMaxDegreeOfParallelism;

        foreach (var partition in usedFactPartitions.OrderBy(partition => partition.Order))
        {
            var orderedOperations = partition.OrderedOperations;
            metrics.EnumerateOrderedOperationsElapsedMilliseconds += partition.EnumerateOrderedOperationsMilliseconds;
            metrics.OrderedOperationCount += orderedOperations.Count;
            metrics.CollectUsedFactsElapsedMilliseconds += partition.CollectUsedFactsMilliseconds;
            metrics.CollectUsedFactsElapsedTicks += partition.CollectUsedFactsTicks;
            metrics.UsedFactCount += partition.UsedFactCount;
            metrics.UsedFactRecordCount += partition.OrderedOperations.Count;
            metrics.MethodOperationNodeProjectionCount += partition.OrderedOperations.Count;

        }

        var callArgumentAndReturnStopwatch = Stopwatch.StartNew();
        AddCallArgumentAndReturnDataFlow(graph);
        callArgumentAndReturnStopwatch.Stop();
        metrics.CallArgumentAndReturnElapsedMilliseconds += callArgumentAndReturnStopwatch.ElapsedMilliseconds;
    }

    private async Task<IReadOnlyList<UsedFactPartition>> RunUsedFactPartitionsAsync(
      IReadOnlyList<IBlockOperation> methodBlocks)
    {
        return await BoundedPartitionWorkWindow.RunAsync(
          methodBlocks,
          _options.EffectiveMaxDegreeOfParallelism,
          AnalyzeUsedFactPartition);
    }

    private static UsedFactPartition AnalyzeUsedFactPartition(IBlockOperation methodBlock, int order)
    {
        var orderedOperationsStopwatch = Stopwatch.StartNew();
        var orderedOperations = methodBlock.DescendantsAndSelf().ToList();
        orderedOperationsStopwatch.Stop();

        var usedFactsStopwatch = Stopwatch.StartNew();
        var usedFactsByOperation = new Dictionary<IOperation, UsedFactRecord>(
          ReferenceEqualityComparer.Instance);
        var usedFactCount = 0;
        foreach (var operation in orderedOperations.AsEnumerable().Reverse())
        {
            var directFacts = DirectUsedFacts(operation).ToArray();
            var childRecords = operation.ChildOperations
              .Where(usedFactsByOperation.ContainsKey)
              .Select(child => usedFactsByOperation[child])
              .ToArray();
            var factCount = directFacts.Length + childRecords.Sum(record => record.FactCount);
            usedFactsByOperation[operation] = new UsedFactRecord(directFacts, childRecords, factCount);
            usedFactCount += factCount;
        }
        usedFactsStopwatch.Stop();

        return new UsedFactPartition(
          order,
          orderedOperations,
          usedFactsByOperation,
          orderedOperationsStopwatch.ElapsedMilliseconds,
          usedFactsStopwatch.ElapsedMilliseconds,
          usedFactsStopwatch.ElapsedTicks,
          usedFactCount);
    }

    private IReadOnlyList<MethodDataFlowPlan> BuildCfgSensitivePartitionPlans(
      IReadOnlyList<UsedFactPartition> usedFactPartitions,
      IReadOnlyList<IBlockOperation> methodBlocks,
      DataFlowOperationIndex operationIndex,
      RoslynCpgGraph graph,
      DataFlowPassMetrics metrics)
    {
        var plans = new List<MethodDataFlowPlan>(usedFactPartitions.Count);
        foreach (var partition in usedFactPartitions.OrderBy(partition => partition.Order))
        {
            var methodBlock = methodBlocks[partition.Order];
            var operationNodes = partition.OrderedOperations
              .Select(operation => operationIndex.NodesByOperation[operation])
              .ToList();
            var parameterDefinitionFacts = new Dictionary<string, DefinitionFact>(StringComparer.Ordinal);
            var parameterNodes = new List<RoslynCpgNode>();
            if (operationIndex.OwningMethods.TryGetValue(methodBlock, out var methodSymbol))
            {
                foreach (var parameter in methodSymbol.Parameters)
                {
                    var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
                    parameterNodes.Add(parameterNode);
                    parameterDefinitionFacts[parameterNode.Id] = DefinitionFactForParameter(parameter);
                }
            }

            var flowNodes = parameterNodes.Concat(operationNodes).ToList();
            var nodesById = flowNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var operationNodeIds = partition.OrderedOperations.ToDictionary(
              operation => operation,
              operation => operationIndex.NodeIdsByOperation[operation],
              (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);
            string? returnNodeId = null;
            string? exitNodeId = null;
            var methodFullName = $"method-partition-{partition.Order}";
            if (operationIndex.OwningMethods.TryGetValue(methodBlock, out var flowMethodSymbol))
            {
              methodFullName = flowMethodSymbol.ToDisplayString();
              var returnNode = GetOrCreateMethodReturnNode(flowMethodSymbol, graph);
              returnNodeId = returnNode.Id;
              nodesById[returnNode.Id] = returnNode;
              if (partition.OrderedOperations.OfType<IReturnOperation>().Any(operation => operation.ReturnedValue is not null))
              {
                var exitNode = GetOrCreateMethodExitNode(flowMethodSymbol, graph);
                exitNodeId = exitNode.Id;
                nodesById[exitNode.Id] = exitNode;
              }
            }
            var flowNodeIds = nodesById.Keys.ToHashSet(StringComparer.Ordinal);
            var flowNeighborsStopwatch = Stopwatch.StartNew();
            var predecessors = SnapshotNeighbors(BuildFlowNeighborsFromCache(flowNodeIds, incoming: true));
            var successors = SnapshotNeighbors(BuildFlowNeighborsFromCache(flowNodeIds, incoming: false));
            flowNeighborsStopwatch.Stop();
            metrics.BuildFlowNeighborsElapsedMilliseconds += flowNeighborsStopwatch.ElapsedMilliseconds;
            plans.Add(new MethodDataFlowPlan(
              partition.Order,
              methodFullName,
              partition.OrderedOperations.ToImmutableArray(),
              operationNodes.ToImmutableArray(),
              partition.UsedFactsByOperation.ToImmutableDictionary(ReferenceEqualityComparer.Instance),
              parameterDefinitionFacts.ToImmutableDictionary(StringComparer.Ordinal),
              nodesById.ToImmutableDictionary(StringComparer.Ordinal),
              operationNodeIds.ToImmutableDictionary(ReferenceEqualityComparer.Instance),
              returnNodeId,
              exitNodeId,
              predecessors.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableArray(),
                StringComparer.Ordinal),
              successors.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableArray(),
                StringComparer.Ordinal)));
        }

        return plans;
    }

    private int RunCfgSensitivePartitionsInOrder(
      IReadOnlyList<MethodDataFlowPlan> plans,
      RoslynCpgGraph graph,
      DataFlowPassMetrics metrics)
    {
        return BoundedPartitionWorkWindow.RunOrdered(
          plans,
          _options.EffectiveMaxDegreeOfParallelism,
          (plan, _) => AnalyzeCfgSensitivePartition(plan, _options.EffectiveDataFlowOptions),
          (partition, order) => CommitCfgSensitivePartition(
            plans[order],
            partition,
            graph,
            metrics));
    }

    private static void CommitCfgSensitivePartition(
      MethodDataFlowPlan plan,
      CfgSensitivePartition partition,
      RoslynCpgGraph graph,
      DataFlowPassMetrics metrics)
    {
        if (partition.Order != plan.Order)
        {
            throw new InvalidOperationException("Candidate batch order does not match its frozen plan.");
        }

        metrics.MethodTelemetry.Add(new RoslynCpgMethodDataFlowTelemetry(
          plan.MethodFullName,
          partition.DefinitionFactCount,
          partition.FlowNodeCount,
          partition.FixpointIterations,
          partition.UnreachableNodeCount,
          partition.GeneratedCandidateCount,
          partition.OverflowReason));

        metrics.CfgSensitiveElapsedMilliseconds += partition.ElapsedMilliseconds;
        metrics.CfgSensitiveCandidateGenerationElapsedMilliseconds += partition.ElapsedMilliseconds;
        metrics.CreateDefinitionFactsElapsedMilliseconds += partition.CreateDefinitionFactsMilliseconds;
        metrics.InitializeCfgSensitiveStateElapsedMilliseconds += partition.InitializeStateMilliseconds;
        metrics.FixpointElapsedMilliseconds += partition.FixpointMilliseconds;
        metrics.ReachingDefinitionEdgeElapsedMilliseconds += partition.ReachingDefinitionEdgeMilliseconds;
        metrics.ValueSourceEdgeElapsedMilliseconds += partition.ValueSourceEdgeMilliseconds;
        metrics.ReturnFlowEdgeElapsedMilliseconds += partition.ReturnFlowEdgeMilliseconds;
        metrics.TerminalFlowEdgeElapsedMilliseconds += partition.TerminalFlowEdgeMilliseconds;
        metrics.CandidateEdgeCount += partition.GeneratedCandidateCount;
        metrics.FlowNodeCount += partition.FlowNodeCount;
        metrics.DefinitionFactCount += partition.DefinitionFactCount;

        if (partition.OverflowReason != RoslynCpgDataFlowOverflowReason.None)
        {
            metrics.SkippedMethodCount += 1;
            return;
        }

        var commitStopwatch = Stopwatch.StartNew();
        foreach (var edge in partition.Edges)
        {
            graph.AddEdge(
              plan.NodesById[edge.SourceNodeId],
              plan.NodesById[edge.TargetNodeId],
              RoslynCpgEdgeKind.DataFlow);
        }
        commitStopwatch.Stop();

        metrics.CfgSensitiveCandidateCommitElapsedMilliseconds += commitStopwatch.ElapsedMilliseconds;
    }

    private static CfgSensitivePartition AnalyzeCfgSensitivePartition(
      MethodDataFlowPlan plan,
      RoslynCpgDataFlowOptions options)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var definitionFactsByNodeId = new Dictionary<string, DefinitionFact>(
          plan.ParameterDefinitionFacts,
          StringComparer.Ordinal);

        var createDefinitionFactsStopwatch = Stopwatch.StartNew();
        foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
        {
            var definedFact = DefinedFact(operationNodePair.First);
            if (definedFact is not null)
            {
                definitionFactsByNodeId[operationNodePair.Second.Id] = definedFact;
            }
        }
        createDefinitionFactsStopwatch.Stop();

        var unreachableNodeCount = CountUnreachableNodes(plan);
        if (definitionFactsByNodeId.Count > options.MaxDefinitionsPerMethod)
        {
            ThrowIfBudgetFailure(
              options,
              plan.MethodFullName,
              RoslynCpgDataFlowOverflowReason.DefinitionLimitExceeded);
            totalStopwatch.Stop();
            return new CfgSensitivePartition(
              plan.Order,
              Array.Empty<DataFlowEdgeCandidate>(),
              totalStopwatch.ElapsedMilliseconds,
              createDefinitionFactsStopwatch.ElapsedMilliseconds,
              InitializeStateMilliseconds: 0,
              FixpointMilliseconds: 0,
              ReachingDefinitionEdgeMilliseconds: 0,
              ValueSourceEdgeMilliseconds: 0,
              ReturnFlowEdgeMilliseconds: 0,
              TerminalFlowEdgeMilliseconds: 0,
              plan.NodesById.Count,
              definitionFactsByNodeId.Count,
              FixpointIterations: 0,
              UnreachableNodeCount: unreachableNodeCount,
              GeneratedCandidateCount: 0,
              OverflowReason: RoslynCpgDataFlowOverflowReason.DefinitionLimitExceeded);
        }

        if (plan.NodesById.Count > options.MaxFlowNodesPerMethod)
        {
            ThrowIfBudgetFailure(
              options,
              plan.MethodFullName,
              RoslynCpgDataFlowOverflowReason.FlowNodeLimitExceeded);
            totalStopwatch.Stop();
            return new CfgSensitivePartition(
              plan.Order,
              Array.Empty<DataFlowEdgeCandidate>(),
              totalStopwatch.ElapsedMilliseconds,
              createDefinitionFactsStopwatch.ElapsedMilliseconds,
              InitializeStateMilliseconds: 0,
              FixpointMilliseconds: 0,
              ReachingDefinitionEdgeMilliseconds: 0,
              ValueSourceEdgeMilliseconds: 0,
              ReturnFlowEdgeMilliseconds: 0,
              TerminalFlowEdgeMilliseconds: 0,
              plan.NodesById.Count,
              definitionFactsByNodeId.Count,
              FixpointIterations: 0,
              UnreachableNodeCount: unreachableNodeCount,
              GeneratedCandidateCount: 0,
              OverflowReason: RoslynCpgDataFlowOverflowReason.FlowNodeLimitExceeded);
        }

        var initializeStateStopwatch = Stopwatch.StartNew();
        var flowNodeIds = plan.NodesById.Keys;
        var inSets = flowNodeIds.ToDictionary(
          nodeId => nodeId,
          _ => new HashSet<string>(StringComparer.Ordinal),
          StringComparer.Ordinal);
        var outSets = flowNodeIds.ToDictionary(
          nodeId => nodeId,
          _ => new HashSet<string>(StringComparer.Ordinal),
          StringComparer.Ordinal);
        var worklist = new Queue<string>(flowNodeIds);
        var queued = new HashSet<string>(flowNodeIds, StringComparer.Ordinal);
        initializeStateStopwatch.Stop();

        var fixpointStopwatch = Stopwatch.StartNew();
        var fixpointIterations = 0;
        while (worklist.Count > 0)
        {
            fixpointIterations += 1;
            var nodeId = worklist.Dequeue();
            queued.Remove(nodeId);
            var incomingDefinitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var predecessorId in plan.Predecessors[nodeId])
            {
                incomingDefinitions.UnionWith(outSets[predecessorId]);
            }

            inSets[nodeId] = incomingDefinitions;
            var updatedOut = ApplyDefinitionTransfer(nodeId, incomingDefinitions, definitionFactsByNodeId);
            if (updatedOut.SetEquals(outSets[nodeId]))
            {
                continue;
            }

            outSets[nodeId] = updatedOut;
            foreach (var successorId in plan.Successors[nodeId])
            {
                if (queued.Add(successorId))
                {
                    worklist.Enqueue(successorId);
                }
            }
        }
        fixpointStopwatch.Stop();

        var edgeStopwatch = Stopwatch.StartNew();
        var edges = new List<DataFlowEdgeCandidate>();
        foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
        {
            foreach (var usedFact in plan.UsedFactsByOperation[operationNodePair.First].EnumerateFacts())
            {
                foreach (var reachingDefinitionId in inSets[operationNodePair.Second.Id])
                {
                    if (definitionFactsByNodeId.TryGetValue(reachingDefinitionId, out var reachingFact) &&
                        FactsMatch(reachingFact, usedFact))
                    {
                        edges.Add(new DataFlowEdgeCandidate(reachingDefinitionId, operationNodePair.Second.Id));
                    }
                }
            }
        }
        edgeStopwatch.Stop();
        var valueSourceStopwatch = Stopwatch.StartNew();
        foreach (var operation in plan.OrderedOperations)
        {
            foreach (var sourceOperation in ValueSourceOperations(operation))
            {
                if (!ReferenceEquals(sourceOperation, operation) &&
                    plan.OperationNodeIds.TryGetValue(sourceOperation, out var sourceNodeId) &&
                    plan.OperationNodeIds.TryGetValue(operation, out var targetNodeId))
                {
                    edges.Add(new DataFlowEdgeCandidate(sourceNodeId, targetNodeId));
                }
            }
        }
        valueSourceStopwatch.Stop();
        var returnFlowStopwatch = Stopwatch.StartNew();
        if (plan.ReturnNodeId is not null && plan.ExitNodeId is not null)
        {
            foreach (var returnOperation in plan.OrderedOperations.OfType<IReturnOperation>().Where(operation => operation.ReturnedValue is not null))
            {
                if (plan.OperationNodeIds.TryGetValue(returnOperation.ReturnedValue!, out var valueNodeId) &&
                    plan.OperationNodeIds.TryGetValue(returnOperation, out var returnOperationNodeId))
                {
                    edges.Add(new DataFlowEdgeCandidate(valueNodeId, plan.ReturnNodeId));
                    edges.Add(new DataFlowEdgeCandidate(plan.ReturnNodeId, plan.ExitNodeId));
                    edges.Add(new DataFlowEdgeCandidate(returnOperationNodeId, plan.ExitNodeId));
                }
            }
        }
        returnFlowStopwatch.Stop();
        var terminalFlowStopwatch = Stopwatch.StartNew();
        if (plan.ReturnNodeId is not null && plan.OrderedOperations.FirstOrDefault() is IBlockOperation methodBlock)
        {
            var terminalOperation = methodBlock.Operations.LastOrDefault();
            if (terminalOperation is not null && !ContainsExplicitReturn(methodBlock) && !StopsSequentialFlow(terminalOperation) &&
                plan.OperationNodeIds.TryGetValue(terminalOperation, out var terminalNodeId))
            {
                edges.Add(new DataFlowEdgeCandidate(terminalNodeId, plan.ReturnNodeId));
            }
        }
        terminalFlowStopwatch.Stop();
        if (edges.Count > options.MaxCandidateEdgesPerMethod)
        {
            ThrowIfBudgetFailure(
              options,
              plan.MethodFullName,
              RoslynCpgDataFlowOverflowReason.CandidateEdgeLimitExceeded);

            totalStopwatch.Stop();
            return new CfgSensitivePartition(
              plan.Order,
              Array.Empty<DataFlowEdgeCandidate>(),
              totalStopwatch.ElapsedMilliseconds,
              createDefinitionFactsStopwatch.ElapsedMilliseconds,
              initializeStateStopwatch.ElapsedMilliseconds,
              fixpointStopwatch.ElapsedMilliseconds,
              edgeStopwatch.ElapsedMilliseconds,
              valueSourceStopwatch.ElapsedMilliseconds,
              returnFlowStopwatch.ElapsedMilliseconds,
              terminalFlowStopwatch.ElapsedMilliseconds,
              plan.NodesById.Count,
              definitionFactsByNodeId.Count,
              fixpointIterations,
              unreachableNodeCount,
              edges.Count,
              RoslynCpgDataFlowOverflowReason.CandidateEdgeLimitExceeded);
        }
        totalStopwatch.Stop();

        return new CfgSensitivePartition(
          plan.Order,
          edges,
          totalStopwatch.ElapsedMilliseconds,
          createDefinitionFactsStopwatch.ElapsedMilliseconds,
          initializeStateStopwatch.ElapsedMilliseconds,
          fixpointStopwatch.ElapsedMilliseconds,
          edgeStopwatch.ElapsedMilliseconds,
          valueSourceStopwatch.ElapsedMilliseconds,
          returnFlowStopwatch.ElapsedMilliseconds,
          terminalFlowStopwatch.ElapsedMilliseconds,
          plan.NodesById.Count,
          definitionFactsByNodeId.Count,
          fixpointIterations,
          unreachableNodeCount,
          edges.Count,
          RoslynCpgDataFlowOverflowReason.None);
    }

    private static void ThrowIfBudgetFailure(
      RoslynCpgDataFlowOptions options,
      string methodFullName,
      RoslynCpgDataFlowOverflowReason overflowReason)
    {
        if (options.OverflowBehavior != RoslynCpgDataFlowOverflowBehavior.FailBuild)
        {
            return;
        }

        throw new InvalidOperationException(
          $"Data-flow {overflowReason} budget exceeded for {methodFullName}.");
    }

    private static int CountUnreachableNodes(MethodDataFlowPlan plan)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var worklist = new Queue<string>(plan.NodesById.Keys.Where(
          nodeId => plan.Predecessors[nodeId].Length == 0));
        while (worklist.Count > 0)
        {
            var nodeId = worklist.Dequeue();
            if (!visited.Add(nodeId))
            {
                continue;
            }

            foreach (var successorId in plan.Successors[nodeId])
            {
                if (!visited.Contains(successorId))
                {
                    worklist.Enqueue(successorId);
                }
            }
        }

        return plan.NodesById.Count - visited.Count;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotNeighbors(
      IReadOnlyDictionary<string, List<string>> neighbors)
    {
        return neighbors.ToDictionary(
          pair => pair.Key,
          pair => (IReadOnlyList<string>)pair.Value.ToArray(),
          StringComparer.Ordinal);
    }

    private Dictionary<string, List<string>> BuildFlowNeighborsFromCache(HashSet<string> flowNodeIds, bool incoming)
    {
        var neighbors = flowNodeIds.ToDictionary(nodeId => nodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var nodeId in flowNodeIds)
        {
            var cachedNeighbors = incoming
              ? GetCachedCfgPredecessors(nodeId)
              : GetCachedCfgSuccessors(nodeId);
            if (cachedNeighbors.Count == 0)
            {
                continue;
            }

            foreach (var neighborNodeId in cachedNeighbors)
            {
                if (flowNodeIds.Contains(neighborNodeId))
                {
                    neighbors[nodeId].Add(neighborNodeId);
                }
            }
        }

        return neighbors;
    }

    private static HashSet<string> ApplyDefinitionTransfer(string nodeId, HashSet<string> incomingDefinitions, Dictionary<string, DefinitionFact> definitionFactsByNodeId)
    {
        var outgoingDefinitions = new HashSet<string>(incomingDefinitions, StringComparer.Ordinal);
        if (!definitionFactsByNodeId.TryGetValue(nodeId, out var definedFact))
        {
            return outgoingDefinitions;
        }

        outgoingDefinitions.RemoveWhere(definitionId =>
          definitionFactsByNodeId.TryGetValue(definitionId, out var priorFact) &&
          FactsConflict(priorFact, definedFact));
        outgoingDefinitions.Add(nodeId);
        return outgoingDefinitions;
    }

    private static bool FactsMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        if (string.Equals(reachingFact.LocationKey, usedFact.LocationKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsContainerMatch(reachingFact, usedFact) || IsPartMatch(reachingFact, usedFact) || IsAliasMatch(reachingFact, usedFact))
        {
            return true;
        }

        if (string.IsNullOrEmpty(reachingFact.BaseKey) || string.IsNullOrEmpty(usedFact.BaseKey))
        {
            return false;
        }

        return string.Equals(reachingFact.BaseKey, usedFact.BaseKey, StringComparison.Ordinal) &&
               string.Equals(reachingFact.LocationKey, usedFact.LocationKey, StringComparison.Ordinal);
    }

    private static bool FactsConflict(DefinitionFact priorFact, DefinitionFact definedFact)
    {
        if (string.Equals(priorFact.LocationKey, definedFact.LocationKey, StringComparison.Ordinal))
        {
            return true;
        }

        var definedRootKey = FactRootKey(definedFact);
        if (!string.IsNullOrEmpty(definedRootKey) &&
            string.Equals(priorFact.BaseKey, definedRootKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (definedFact.Category is "field" or "property" &&
            string.Equals(priorFact.BaseKey, definedFact.BaseKey, StringComparison.Ordinal))
        {
            return IsAliasMatch(priorFact, definedFact) ||
                   string.Equals(priorFact.PathKey, definedFact.PathKey, StringComparison.Ordinal);
        }

        if (definedFact.Category is "call" && priorFact.Category == "call")
        {
            return string.Equals(priorFact.LocationKey, definedFact.LocationKey, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsContainerMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        var reachingRootKey = FactRootKey(reachingFact);
        return !string.IsNullOrEmpty(usedFact.BaseKey) &&
               !string.IsNullOrEmpty(reachingRootKey) &&
               string.Equals(reachingRootKey, usedFact.BaseKey, StringComparison.Ordinal);
    }

    private static bool IsPartMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        var usedRootKey = FactRootKey(usedFact);
        return !string.IsNullOrEmpty(reachingFact.BaseKey) &&
               !string.IsNullOrEmpty(usedRootKey) &&
               string.Equals(reachingFact.BaseKey, usedRootKey, StringComparison.Ordinal);
    }

    private static bool IsAliasMatch(DefinitionFact left, DefinitionFact right)
    {
        return !string.IsNullOrEmpty(left.BaseKey) &&
               !string.IsNullOrEmpty(right.BaseKey) &&
               string.Equals(left.BaseKey, right.BaseKey, StringComparison.Ordinal) &&
               string.Equals(left.PathKey, right.PathKey, StringComparison.Ordinal);
    }

    private static string? FactRootKey(DefinitionFact fact)
    {
        return fact.BaseKey ?? fact.LocationKey;
    }

    private void AddCallArgumentAndReturnDataFlow(RoslynCpgGraph graph)
    {
        foreach (var invocation in _operationNodes.Keys.OfType<IInvocationOperation>())
        {
            var targetMethod = invocation.TargetMethod;
            if (targetMethod is null)
            {
                continue;
            }

            var callSiteNode = FindCallSiteNode(invocation, graph);
            if (callSiteNode is null)
            {
                continue;
            }

            var candidateMethods = ResolveCallTargetCandidates(invocation, targetMethod).ToList();
            var preferredCandidates = PreferCallTargets(candidateMethods, targetMethod, invocation.Instance?.Type).ToList();
            var effectiveTargets = preferredCandidates.Count > 0 ? preferredCandidates : candidateMethods;
            if (effectiveTargets.Count == 0)
            {
                effectiveTargets.Add(targetMethod);
            }

            foreach (var candidateMethod in effectiveTargets.Where(IsInternalMethod).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
            {
                AddArgumentToParameterFlows(invocation, candidateMethod, graph);

                var returnNode = GetOrCreateMethodReturnNode(candidateMethod, graph);
                graph.AddEdge(returnNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            }
        }

        foreach (var propertyReference in _operationNodes.Keys.OfType<IPropertyReferenceOperation>())
        {
            AddPropertyAccessorSummaryDataFlow(propertyReference, graph);
        }
    }

    private void AddPropertyAccessorSummaryDataFlow(IPropertyReferenceOperation propertyReference, RoslynCpgGraph graph)
    {
        AddPropertyGetterSummaryDataFlow(propertyReference, graph);
        AddPropertySetterSummaryDataFlow(propertyReference, graph);
    }

    private void AddPropertyGetterSummaryDataFlow(IPropertyReferenceOperation propertyReference, RoslynCpgGraph graph)
    {
        var getterMethod = propertyReference.Property.GetMethod;
        if (getterMethod is null)
        {
            return;
        }

        var getter = CanonicalMethodSymbol(getterMethod);
        if (!IsInternalMethod(getter))
        {
            return;
        }

        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, getter, graph);
        AddPropertyAccessorParameterFlows(propertyReference, getter, callSiteNode, includesSetterValue: false, setterValue: null, graph);

        var returnNode = GetOrCreateMethodReturnNode(getter, graph);
        graph.AddEdge(returnNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        if (callSiteNode is not null)
        {
            graph.AddEdge(returnNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            graph.AddEdge(callSiteNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private void AddPropertySetterSummaryDataFlow(IPropertyReferenceOperation propertyReference, RoslynCpgGraph graph)
    {
        if (!IsPropertyWrite(propertyReference))
        {
            return;
        }

        var setterMethod = propertyReference.Property.SetMethod;
        if (setterMethod is null)
        {
            return;
        }

        var setter = CanonicalMethodSymbol(setterMethod);
        if (!IsInternalMethod(setter))
        {
            return;
        }

        if (propertyReference.Parent is not ISimpleAssignmentOperation assignment)
        {
            return;
        }

        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, setter, graph);
        AddPropertyAccessorParameterFlows(propertyReference, setter, callSiteNode, includesSetterValue: true, assignment.Value, graph);

        graph.AddEdge(GetOrCreateOperationNode(assignment.Value, graph), propertyNode, RoslynCpgEdgeKind.DataFlow);
        if (callSiteNode is not null)
        {
            graph.AddEdge(callSiteNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private void AddPropertyAccessorParameterFlows(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod, RoslynCpgNode? callSiteNode, bool includesSetterValue, IOperation? setterValue, RoslynCpgGraph graph)
    {
        var parameterIndex = 0;
        if (propertyReference.Instance is not null && accessorMethod.Parameters.Length > parameterIndex)
        {
            var receiverNode = GetOrCreateOperationNode(propertyReference.Instance, graph);
            var receiverParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex += 1;
        }

        foreach (var argument in propertyReference.Arguments)
        {
            if (parameterIndex >= accessorMethod.Parameters.Length || argument.Value is null)
            {
                continue;
            }

            var argumentNode = GetOrCreateOperationNode(argument.Value, graph);
            var argumentParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(argumentNode, argumentParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(argumentNode, argumentParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex += 1;
        }

        if (includesSetterValue &&
            setterValue is not null &&
            parameterIndex < accessorMethod.Parameters.Length)
        {
            var valueNode = GetOrCreateOperationNode(setterValue, graph);
            var setterValueParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(valueNode, setterValueParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(valueNode, setterValueParameterNode, RoslynCpgEdgeKind.DataFlow);
            if (callSiteNode is not null)
            {
                graph.AddEdge(valueNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            }
        }
    }

    private void AddArgumentToParameterFlows(IInvocationOperation invocation, IMethodSymbol candidateMethod, RoslynCpgGraph graph)
    {
        var parameterIndex = 0;

        if (candidateMethod.IsExtensionMethod && invocation.Instance is not null && candidateMethod.Parameters.Length > 0)
        {
            var receiverNode = GetOrCreateOperationNode(invocation.Instance, graph);
            var receiverParameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[0], graph);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex = 1;
        }

        for (var argumentIndex = 0; argumentIndex < invocation.Arguments.Length && parameterIndex < candidateMethod.Parameters.Length; argumentIndex += 1, parameterIndex += 1)
        {
            var argumentValue = invocation.Arguments[argumentIndex].Value;
            if (argumentValue is null)
            {
                continue;
            }

            var argumentNode = GetOrCreateOperationNode(argumentValue, graph);
            var parameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(argumentNode, parameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(argumentNode, parameterNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private RoslynCpgNode? FindCallSiteNode(IInvocationOperation invocationOperation, RoslynCpgGraph graph)
    {
        return _callSiteNodesByInvocation.TryGetValue(invocationOperation, out var callSiteNode)
          ? callSiteNode
          : null;
    }

    private RoslynCpgNode? FindPropertyAccessorCallSiteNode(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod, RoslynCpgGraph graph)
    {
        return _propertyAccessorCallSiteNodesByKey.TryGetValue(
          PropertyAccessorCallSiteKey(propertyReference, accessorMethod),
          out var callSiteNode)
            ? callSiteNode
            : null;
    }

    private static DefinitionFact? DefinedFact(IOperation operation)
    {
        return operation switch
        {
            IVariableDeclaratorOperation declarator => DefinitionFactForSymbol(declarator.Symbol),
            ISimpleAssignmentOperation assignment => DefinitionFactForAssignmentTarget(assignment.Target),
            IInvocationOperation invocation => DefinitionFactForInvocation(invocation),
            IPropertyReferenceOperation propertyReference when IsPropertyRead(propertyReference) =>
              DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance),
            _ => null,
        };
    }

    private static IEnumerable<IOperation> ValueSourceOperations(IOperation operation)
    {
        switch (operation)
        {
            case IVariableDeclaratorOperation declarator when declarator.Initializer?.Value is { } initializerValue:
                yield return initializerValue;
                break;
            case ISimpleAssignmentOperation assignment:
                yield return assignment.Value;
                break;
            case IInvocationOperation invocation:
                if (invocation.Instance is not null)
                {
                    yield return invocation.Instance;
                }

                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Value is not null)
                    {
                        yield return argument.Value;
                    }
                }

                break;
            case IReturnOperation returnOperation when returnOperation.ReturnedValue is not null:
                yield return returnOperation.ReturnedValue;
                break;
        }
    }

    private static IEnumerable<DefinitionFact> DirectUsedFacts(IOperation operation)
    {
        switch (operation)
        {
            case ILocalReferenceOperation localReference:
                yield return DefinitionFactForSymbol(localReference.Local);
                yield break;
            case IParameterReferenceOperation parameterReference:
                yield return DefinitionFactForParameter(parameterReference.Parameter);
                yield break;
            case IFieldReferenceOperation fieldReference:
                yield return DefinitionFactForField(fieldReference.Field, fieldReference.Instance);
                yield break;
            case IPropertyReferenceOperation propertyReference:
                yield return DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance);
                yield break;
        }
    }

    private static DefinitionFact DefinitionFactForAssignmentTarget(IOperation target)
    {
        return target switch
        {
            ILocalReferenceOperation localReference => DefinitionFactForSymbol(localReference.Local),
            IParameterReferenceOperation parameterReference => DefinitionFactForParameter(parameterReference.Parameter),
            IFieldReferenceOperation fieldReference => DefinitionFactForField(fieldReference.Field, fieldReference.Instance),
            IPropertyReferenceOperation propertyReference => DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance),
            _ => new DefinitionFact(target.Kind.ToString(), null, "unknown"),
        };
    }

    private static DefinitionFact DefinitionFactForInvocation(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        var locationKey = targetMethod is null
          ? $"call:{invocation.Syntax.SpanStart}:{invocation.Syntax.Span.End}"
          : $"call:{ComposeMethodFullName(targetMethod)}";
        return new DefinitionFact(locationKey, ReceiverKey(invocation.Instance), "call", ComposeInvocationPathKey(invocation));
    }

    private static DefinitionFact DefinitionFactForSymbol(ISymbol symbol)
    {
        return new DefinitionFact(SymbolId(symbol), null, symbol.Kind.ToString(), SymbolId(symbol));
    }

    private static DefinitionFact DefinitionFactForParameter(IParameterSymbol parameter)
    {
        return new DefinitionFact(SymbolId(parameter), null, "parameter", SymbolId(parameter));
    }

    private static DefinitionFact DefinitionFactForField(IFieldSymbol field, IOperation? instance)
    {
        var baseKey = ReceiverKey(instance);
        var locationKey = baseKey is null
          ? $"field:{SymbolId(field)}"
          : $"field:{baseKey}.{field.Name}";
        return new DefinitionFact(locationKey, baseKey, "field", field.Name);
    }

    private static DefinitionFact DefinitionFactForProperty(IPropertySymbol property, IOperation? instance)
    {
        var baseKey = ReceiverKey(instance);
        var propertyKey = property.Parameters.Length == 0
          ? property.Name
          : $"{property.Name}:{ComposePropertySignature(property)}";
        var locationKey = baseKey is null
          ? $"property:{SymbolId(property)}"
          : $"property:{baseKey}.{propertyKey}";
        return new DefinitionFact(locationKey, baseKey, "property", propertyKey);
    }

    private static string ComposeInvocationPathKey(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        if (targetMethod is not null)
        {
            return ComposeMethodLookupKey(targetMethod);
        }

        return $"invoke:{invocation.Syntax.SpanStart}:{invocation.Syntax.Span.End}";
    }

    private static bool IsPropertyRead(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent is not ISimpleAssignmentOperation assignment ||
               !ReferenceEquals(assignment.Target, propertyReference);
    }

    private static bool IsPropertyWrite(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent is ISimpleAssignmentOperation assignment &&
               ReferenceEquals(assignment.Target, propertyReference);
    }

    private static string? ReceiverKey(IOperation? instance)
    {
        if (instance is null)
        {
            return null;
        }

        return instance switch
        {
            IInstanceReferenceOperation instanceReference => ComposeTypeFullName(instanceReference.Type),
            ILocalReferenceOperation localReference => $"local:{SymbolId(localReference.Local)}",
            IParameterReferenceOperation parameterReference => $"param:{SymbolId(parameterReference.Parameter)}",
            IFieldReferenceOperation fieldReference => DefinitionFactForField(fieldReference.Field, fieldReference.Instance).LocationKey,
            IPropertyReferenceOperation propertyReference => DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance).LocationKey,
            _ => $"op:{instance.Kind}:{instance.Syntax.SpanStart}:{instance.Syntax.Span.End}",
        };
    }
}
}
