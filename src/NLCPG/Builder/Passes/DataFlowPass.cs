using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class DataFlowPass : INLCPGPass
    {
        internal static DataFlowPass Instance { get; } = new();

        private DataFlowPass()
        {
        }

        public string Name => nameof(DataFlowPass);

        // 触发数据流 pass，写入 reaching-definition 与摘要流边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunDataFlowPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        private sealed record UsedFactRecord(
            IReadOnlyList<DefinitionFact> DirectFacts,
            IReadOnlyList<UsedFactRecord> ChildRecords,
            int FactCount)
        {
            // 展开当前记录及其子记录中的全部使用事实。
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
            IOperation[] OrderedOperations,
            Dictionary<IOperation, UsedFactRecord> UsedFactsByOperation,
            int RetainedRecordCount);

        private sealed record DataFlowMethodPartition(
            IBlockOperation MethodBlock,
            IOperation[] OrderedOperations,
            IMethodSymbol? OwningMethod);

        private sealed class MethodDataFlowPlan
        {
            internal MethodDataFlowPlan(int order, string methodFullName, IOperation[] orderedOperations, NLCPGNode[] flowNodes, NLCPGNode[] operationNodes, Dictionary<IOperation, UsedFactRecord> usedFactsByOperation, Dictionary<NLCPGNode, DefinitionFact> parameterDefinitionFacts, Dictionary<IOperation, NLCPGNode> operationNodesByOperation, NLCPGNode? returnNode, NLCPGNode? exitNode, Dictionary<NLCPGNode, NLCPGNode[]> predecessors, Dictionary<NLCPGNode, NLCPGNode[]> successors)
            {
                Order = order;
                MethodFullName = methodFullName;
                OrderedOperations = orderedOperations;
                FlowNodes = flowNodes;
                OperationNodes = operationNodes;
                UsedFactsByOperation = usedFactsByOperation;
                ParameterDefinitionFacts = parameterDefinitionFacts;
                OperationNodesByOperation = operationNodesByOperation;
                ReturnNode = returnNode;
                ExitNode = exitNode;
                Predecessors = predecessors;
                Successors = successors;
            }

            internal int Order { get; }
            internal string MethodFullName { get; }
            internal IOperation[] OrderedOperations { get; private set; }
            internal NLCPGNode[] FlowNodes { get; private set; }
            internal NLCPGNode[] OperationNodes { get; private set; }
            internal Dictionary<IOperation, UsedFactRecord> UsedFactsByOperation { get; private set; }
            internal Dictionary<NLCPGNode, DefinitionFact> ParameterDefinitionFacts { get; private set; }
            internal Dictionary<IOperation, NLCPGNode> OperationNodesByOperation { get; private set; }
            internal NLCPGNode? ReturnNode { get; private set; }
            internal NLCPGNode? ExitNode { get; private set; }
            internal Dictionary<NLCPGNode, NLCPGNode[]> Predecessors { get; private set; }
            internal Dictionary<NLCPGNode, NLCPGNode[]> Successors { get; private set; }

            internal void Release()
            {
                OrderedOperations = Array.Empty<IOperation>();
                FlowNodes = Array.Empty<NLCPGNode>();
                OperationNodes = Array.Empty<NLCPGNode>();
                UsedFactsByOperation = new Dictionary<IOperation, UsedFactRecord>(ReferenceEqualityComparer.Instance);
                ParameterDefinitionFacts = new Dictionary<NLCPGNode, DefinitionFact>();
                OperationNodesByOperation = new Dictionary<IOperation, NLCPGNode>(ReferenceEqualityComparer.Instance);
                ReturnNode = null;
                ExitNode = null;
                Predecessors = new Dictionary<NLCPGNode, NLCPGNode[]>();
                Successors = new Dictionary<NLCPGNode, NLCPGNode[]>();
            }
        }

        private static CpgEdgeCandidate CreateDataFlowCandidate(NLCPGNode sourceNode, NLCPGNode targetNode)
        {
            return new CpgEdgeCandidate(
              sourceNode.StableAnchor ?? throw new InvalidOperationException("Data-flow source nodes require stable anchors."),
              targetNode.StableAnchor ?? throw new InvalidOperationException("Data-flow target nodes require stable anchors."),
              NLCPGEdgeKind.DataFlow,
              StructuredLabel: null,
              ContextId: null,
              CallSiteContext: null);
        }

        private sealed class DataFlowCandidateCollector
        {
            private readonly List<CpgEdgeCandidate> _uniqueCandidates = new();
            private readonly HashSet<CpgEdgeCandidate> _seenCandidates = new();

            internal int RawCandidateCount { get; private set; }

            internal int UniqueCandidateCount => _uniqueCandidates.Count;

            internal bool TryAdd(NLCPGNode sourceNode, NLCPGNode targetNode, int maxCandidateEdges)
            {
                RawCandidateCount += 1;
                if (RawCandidateCount > maxCandidateEdges)
                {
                    return false;
                }

                var candidate = CreateDataFlowCandidate(sourceNode, targetNode);
                if (_seenCandidates.Add(candidate))
                {
                    _uniqueCandidates.Add(candidate);
                }

                return true;
            }

            internal LocalFlowCandidateSet ToCandidateSet()
            {
                return new LocalFlowCandidateSet(_uniqueCandidates);
            }
        }

        private sealed class DefinitionFactIndex
        {
            private readonly Dictionary<string, List<NLCPGNode>> _byLocation = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<NLCPGNode>> _byRoot = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<NLCPGNode>> _byBase = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<NLCPGNode>> _byBaseAndPath = new(StringComparer.Ordinal);

            internal DefinitionFactIndex(IReadOnlyDictionary<NLCPGNode, DefinitionFact> factsByNode)
            {
                foreach (var pair in factsByNode)
                {
                    var fact = pair.Value;
                    Add(_byLocation, fact.LocationKey, pair.Key);
                    Add(_byRoot, FactRootKey(fact), pair.Key);
                    Add(_byBase, fact.BaseKey, pair.Key);
                    if (!string.IsNullOrEmpty(fact.BaseKey) && !string.IsNullOrEmpty(fact.PathKey))
                    {
                        Add(_byBaseAndPath, ComposeBaseAndPathKey(fact.BaseKey, fact.PathKey), pair.Key);
                    }
                }
            }

            internal bool TryGetCandidates(DefinitionFact usedFact, ulong[] reachingDefinitions, int reachingOffset, int wordsPerSet, IReadOnlyDictionary<NLCPGNode, int> flowNodeOrdinals, out IReadOnlyList<NLCPGNode> candidates)
            {
                if (string.IsNullOrEmpty(usedFact.LocationKey))
                {
                    candidates = Array.Empty<NLCPGNode>();
                    return false;
                }

                var matches = new List<NLCPGNode>();
                var seen = new HashSet<NLCPGNode>();
                AddReachable(_byLocation, usedFact.LocationKey, reachingDefinitions, reachingOffset, wordsPerSet, flowNodeOrdinals, seen, matches);
                AddReachable(_byRoot, usedFact.BaseKey, reachingDefinitions, reachingOffset, wordsPerSet, flowNodeOrdinals, seen, matches);
                AddReachable(_byBase, FactRootKey(usedFact), reachingDefinitions, reachingOffset, wordsPerSet, flowNodeOrdinals, seen, matches);
                if (!string.IsNullOrEmpty(usedFact.BaseKey) && !string.IsNullOrEmpty(usedFact.PathKey))
                {
                    AddReachable(_byBaseAndPath, ComposeBaseAndPathKey(usedFact.BaseKey, usedFact.PathKey), reachingDefinitions, reachingOffset, wordsPerSet, flowNodeOrdinals, seen, matches);
                }

                candidates = matches;
                return true;
            }

            private static void Add(Dictionary<string, List<NLCPGNode>> index, string? key, NLCPGNode node)
            {
                if (string.IsNullOrEmpty(key))
                {
                    return;
                }

                if (!index.TryGetValue(key, out var nodes))
                {
                    nodes = new List<NLCPGNode>();
                    index[key] = nodes;
                }

                nodes.Add(node);
            }

            private static void AddReachable(IReadOnlyDictionary<string, List<NLCPGNode>> index, string? key, ulong[] reachingDefinitions, int reachingOffset, int wordsPerSet, IReadOnlyDictionary<NLCPGNode, int> flowNodeOrdinals, HashSet<NLCPGNode> seen, List<NLCPGNode> matches)
            {
                if (string.IsNullOrEmpty(key) || !index.TryGetValue(key, out var nodes))
                {
                    return;
                }

                foreach (var node in nodes)
                {
                    if (flowNodeOrdinals.TryGetValue(node, out var ordinal) &&
                        IsBitSet(reachingDefinitions, reachingOffset, ordinal) &&
                        seen.Add(node))
                    {
                        matches.Add(node);
                    }
                }
            }

            private static string ComposeBaseAndPathKey(string baseKey, string pathKey)
            {
                return $"{baseKey}\u001F{pathKey}";
            }
        }

        private static NLCPGNode ResolveCandidateNode(NLCPGGraph graph, StableNodeAnchor anchor)
        {
            return graph.AddNode(new NLCPGNode(
              anchor.Kind,
              anchor.Kind.ToString(),
              StableAnchor: anchor));
        }

        private sealed record CfgSensitivePartition(
          int Order,
          LocalFlowCandidateSet Candidates,
          NLCPGDataFlowOverflowReason OverflowReason,
          NLCPGDataFlowMethodMetrics Metrics);

        private sealed record CfgSensitiveWorkResult(
            MethodDataFlowPlan Plan,
            CfgSensitivePartition Partition);

        internal void RunDataFlowPass(NLCPGBuildContext context)
        {
            AddReachingDefinitionDataFlow(context);
        }

        private void AddReachingDefinitionDataFlow(NLCPGBuildContext context)
        {
            var graph = context.Graph;
            var methodPartitions = CreateDataFlowMethodPartitions(context.OperationInventory);
            var operationIndex = CreateDataFlowOperationIndex(
              context.OperationInventory,
              methodPartitions.Select(partition => (IOperation)partition.MethodBlock).ToArray());
            RunDataFlowPipeline(methodPartitions, operationIndex, graph);
            // 方法内数据流补完后，再单独处理跨调用/属性访问的摘要流。
            AddCallArgumentAndReturnDataFlow(context);
        }

        private IReadOnlyList<DataFlowMethodPartition> CreateDataFlowMethodPartitions(IReadOnlyList<OperationInventoryEntry> operationInventory)
        {
            var entriesByMethodRoot = new Dictionary<IOperation, List<OperationInventoryEntry>>(
              (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);
            foreach (var entry in operationInventory)
            {
                if (!entriesByMethodRoot.TryGetValue(entry.MethodRoot, out var entries))
                {
                    entries = new List<OperationInventoryEntry>();
                    entriesByMethodRoot[entry.MethodRoot] = entries;
                }

                entries.Add(entry);
            }

            var partitions = new List<DataFlowMethodPartition>();
            // 只抽出真正的方法根 block，避免局部 block 被当成独立数据流单元。
            foreach (var rootEntry in operationInventory)
            {
                if (!rootEntry.IsRoot || rootEntry.Operation is not IBlockOperation methodBlock || !IsMethodRootBlock(methodBlock))
                {
                    continue;
                }

                partitions.Add(new DataFlowMethodPartition(
                  methodBlock,
                  entriesByMethodRoot[rootEntry.MethodRoot]
                    .Select(entry => entry.Operation)
                    .ToArray(),
                  rootEntry.OwningMethod));
            }

            return partitions;
        }

        private static UsedFactPartition AnalyzeUsedFactPartition(DataFlowMethodPartition methodPartition, int order)
        {
            var orderedOperations = methodPartition.OrderedOperations;
            var usedFactsByOperation = new Dictionary<IOperation, UsedFactRecord>(
              ReferenceEqualityComparer.Instance);
            var retainedRecordCount = orderedOperations.Length;
            for (var operationIndex = orderedOperations.Length - 1; operationIndex >= 0; operationIndex -= 1)
            {
                var operation = orderedOperations[operationIndex];
                var directFacts = DirectUsedFacts(operation).ToArray();
                var childRecords = operation.ChildOperations
                  .Where(usedFactsByOperation.ContainsKey)
                  .Select(child => usedFactsByOperation[child])
                  .ToArray();
                var factCount = directFacts.Length + childRecords.Sum(record => record.FactCount);
                usedFactsByOperation[operation] = new UsedFactRecord(directFacts, childRecords, factCount);
                retainedRecordCount += factCount;
            }

            return new UsedFactPartition(
              order,
              orderedOperations,
              usedFactsByOperation,
              retainedRecordCount);
        }

        private void RunDataFlowPipeline(IReadOnlyList<DataFlowMethodPartition> methodPartitions, DataFlowOperationIndex operationIndex, NLCPGGraph graph)
        {
            _concurrencyPool.CommitTwoStageOrdered(
              methodPartitions,
              new NL.Concurrency.ConcurrencyWindowOptions(
                _options.EffectiveMaxDegreeOfParallelism,
                _options.EffectiveOrderedResultReorderAllowance,
                _options.EffectiveMaxOrderedResultRecordCount),
              AnalyzeUsedFactPartition,
              (usedFacts, order) => BuildCfgSensitivePartitionPlan(usedFacts, methodPartitions[order], operationIndex, graph),
              (plan, _) => new CfgSensitiveWorkResult(plan, AnalyzeCfgSensitivePartition(plan, _options.EffectiveDataFlowOptions)),
              (result, order) =>
              {
                  if (result.Plan.Order != order)
                  {
                      throw new InvalidOperationException("Data-flow plan order does not match the ordered commit slot.");
                  }

                   try
                   {
                       _dataFlowMethodMetrics.Add(result.Partition.Metrics);
                       CommitCfgSensitivePartition(result.Plan, result.Partition, graph);
                  }
                  finally
                  {
                      result.Plan.Release();
                  }
              },
              collectedRetainedRecordCount: partition => partition.RetainedRecordCount,
              resultRetainedRecordCount: result => result.Plan.FlowNodes.Length + result.Partition.Candidates.EdgeCandidates.Count);
        }

        private MethodDataFlowPlan BuildCfgSensitivePartitionPlan(UsedFactPartition partition, DataFlowMethodPartition methodPartition, DataFlowOperationIndex operationIndex, NLCPGGraph graph)
        {
                var orderedOperations = partition.OrderedOperations;
                var operationNodes = new NLCPGNode[orderedOperations.Length];
                var operationNodesByOperation = new Dictionary<IOperation, NLCPGNode>(
                  orderedOperations.Length,
                  ReferenceEqualityComparer.Instance);
                // 先建立 operation -> node 的方法内投影，避免后续阶段重复查全局索引。
                for (var operationOrdinal = 0; operationOrdinal < orderedOperations.Length; operationOrdinal += 1)
                {
                    var operation = orderedOperations[operationOrdinal];
                    var operationNode = operationIndex.NodesByOperation[operation];
                    operationNodes[operationOrdinal] = operationNode;
                    operationNodesByOperation[operation] = operationNode;
                }

                var parameterDefinitionFacts = new Dictionary<NLCPGNode, DefinitionFact>();
                var flowNodes = new List<NLCPGNode>(operationNodes.Length + 4);
                var flowNodeSet = new HashSet<NLCPGNode>();
                if (methodPartition.OwningMethod is IMethodSymbol methodSymbol)
                {
                    // 参数节点也参与 reaching-definition，因此提前放进 flow graph。
                    foreach (var parameter in methodSymbol.Parameters)
                    {
                        var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
                        AddUniqueFlowNode(flowNodes, flowNodeSet, parameterNode);
                        parameterDefinitionFacts[parameterNode] = DefinitionFactForParameter(parameter);
                    }
                }

                foreach (var operationNode in operationNodes)
                {
                    AddUniqueFlowNode(flowNodes, flowNodeSet, operationNode);
                }

                NLCPGNode? returnNode = null;
                NLCPGNode? exitNode = null;
                var methodFullName = $"method-partition-{partition.Order}";
                if (methodPartition.OwningMethod is IMethodSymbol flowMethodSymbol)
                {
                    methodFullName = flowMethodSymbol.ToDisplayString();
                    // return/exit 只在方法实际需要返回值或退出流时加入计划。
                    returnNode = GetOrCreateMethodReturnNode(flowMethodSymbol, graph);
                    AddUniqueFlowNode(flowNodes, flowNodeSet, returnNode);
                    if (orderedOperations.OfType<IReturnOperation>().Any(operation => operation.ReturnedValue is not null))
                    {
                        exitNode = GetOrCreateMethodExitNode(flowMethodSymbol, graph);
                        AddUniqueFlowNode(flowNodes, flowNodeSet, exitNode);
                    }
                }

                var predecessors = SnapshotNeighbors(BuildFlowNeighborsFromCache(flowNodeSet, incoming: true));
                var successors = SnapshotNeighbors(BuildFlowNeighborsFromCache(flowNodeSet, incoming: false));
                return new MethodDataFlowPlan(
                  partition.Order,
                  methodFullName,
                  orderedOperations,
                  flowNodes.ToArray(),
                  operationNodes,
                  partition.UsedFactsByOperation,
                  parameterDefinitionFacts,
                  operationNodesByOperation,
                  returnNode,
                  exitNode,
                  predecessors,
                  successors);
        }

        private static void CommitCfgSensitivePartition(MethodDataFlowPlan plan, CfgSensitivePartition partition, NLCPGGraph graph)
        {
            if (partition.Order != plan.Order)
            {
                throw new InvalidOperationException("Candidate batch order does not match its frozen plan.");
            }

            if (partition.OverflowReason != NLCPGDataFlowOverflowReason.None)
            {
                return;
            }

            // 真正的图提交只发生在这里，保证并行求解阶段不修改共享图。
            foreach (var edge in partition.Candidates.EdgeCandidates)
            {
                graph.AddEdge(
                    ResolveCandidateNode(graph, edge.SourceAnchor),
                    ResolveCandidateNode(graph, edge.TargetAnchor),
                    edge.Kind,
                    edge.StructuredLabel,
                    edge.ContextId,
                    edge.CallSiteContext);
            }
        }

        private static CfgSensitivePartition AnalyzeCfgSensitivePartition(MethodDataFlowPlan plan, NLCPGDataFlowOptions options)
        {
            var definitionFactsByNode = new Dictionary<NLCPGNode, DefinitionFact>(
              plan.ParameterDefinitionFacts);

            // 第一阶段：把“哪些节点定义了什么”整理成节点索引。
            foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
            {
                var definedFact = DefinedFact(operationNodePair.First);
                if (definedFact is not null)
                {
                    definitionFactsByNode[operationNodePair.Second] = definedFact;
                }
            }
            var wordsPerSet = BitSetWordCount(plan.FlowNodes.Length);
            // 预算先于 fixpoint 检查，超限时直接按策略跳过或失败。
            if (definitionFactsByNode.Count > options.MaxDefinitionsPerMethod)
            {
                ThrowIfBudgetFailure(
                  options,
                  plan.MethodFullName,
                  NLCPGDataFlowOverflowReason.DefinitionLimitExceeded);
                return new CfgSensitivePartition(
                  plan.Order,
                  new LocalFlowCandidateSet(Array.Empty<CpgEdgeCandidate>()),
                  NLCPGDataFlowOverflowReason.DefinitionLimitExceeded,
                  CreateDataFlowMetrics(
                    plan,
                    wordsPerSet,
                    definitionFactsByNode.Count,
                    0,
                    null,
                    NLCPGDataFlowOverflowReason.DefinitionLimitExceeded));
            }

            if (plan.FlowNodes.Length > options.MaxFlowNodesPerMethod)
            {
                ThrowIfBudgetFailure(
                  options,
                  plan.MethodFullName,
                  NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded);
                return new CfgSensitivePartition(
                  plan.Order,
                  new LocalFlowCandidateSet(Array.Empty<CpgEdgeCandidate>()),
                  NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded,
                  CreateDataFlowMetrics(
                    plan,
                    wordsPerSet,
                    definitionFactsByNode.Count,
                    0,
                    null,
                    NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded));
            }

            // 第二阶段：用方法局部整数编号和 bitset 初始化 in/out 集，避免节点 HashSet 的迭代分配。
            var flowNodes = plan.FlowNodes;
            var flowNodeOrdinals = flowNodes
              .Select((node, ordinal) => (node, ordinal))
              .ToDictionary(entry => entry.node, entry => entry.ordinal);
            var inSets = new ulong[checked(flowNodes.Length * wordsPerSet)];
            var outSets = new ulong[checked(flowNodes.Length * wordsPerSet)];
            var incomingScratch = new ulong[wordsPerSet];
            var updatedScratch = new ulong[wordsPerSet];
            var worklist = new Queue<int>(flowNodes.Length);
            var queued = new bool[flowNodes.Length];
            for (var ordinal = 0; ordinal < flowNodes.Length; ordinal += 1)
            {
                worklist.Enqueue(ordinal);
                queued[ordinal] = true;
            }
            var definitionFactsByOrdinal = new DefinitionFact?[flowNodes.Length];
            foreach (var definitionFactPair in definitionFactsByNode)
            {
                if (flowNodeOrdinals.TryGetValue(definitionFactPair.Key, out var definitionOrdinal))
                {
                    definitionFactsByOrdinal[definitionOrdinal] = definitionFactPair.Value;
                }
            }
            // 第三阶段：标准 worklist fixpoint，按 predecessor/out 集传播 reaching definitions。
            var worklistIterations = 0;
            while (worklist.Count > 0)
            {
                var nodeOrdinal = worklist.Dequeue();
                queued[nodeOrdinal] = false;
                worklistIterations += 1;
                var node = flowNodes[nodeOrdinal];
                Array.Clear(incomingScratch, 0, incomingScratch.Length);
                foreach (var predecessorNode in plan.Predecessors[node])
                {
                    if (flowNodeOrdinals.TryGetValue(predecessorNode, out var predecessorOrdinal))
                    {
                        OrBitSet(incomingScratch, outSets, predecessorOrdinal * wordsPerSet, wordsPerSet);
                    }
                }

                var nodeOffset = nodeOrdinal * wordsPerSet;
                CopyBitSet(inSets, nodeOffset, incomingScratch, wordsPerSet);
                Array.Copy(incomingScratch, updatedScratch, wordsPerSet);
                if (definitionFactsByOrdinal[nodeOrdinal] is { } definedFact)
                {
                    ApplyDefinitionTransfer(
                      updatedScratch,
                      nodeOrdinal,
                      wordsPerSet,
                      definedFact,
                      definitionFactsByOrdinal);
                }

                if (BitSetEquals(outSets, nodeOffset, updatedScratch, wordsPerSet))
                {
                    continue;
                }

                CopyBitSet(outSets, nodeOffset, updatedScratch, wordsPerSet);
                foreach (var successorNode in plan.Successors[node])
                {
                    if (flowNodeOrdinals.TryGetValue(successorNode, out var successorOrdinal) && !queued[successorOrdinal])
                    {
                        queued[successorOrdinal] = true;
                        worklist.Enqueue(successorOrdinal);
                    }
                }
            }
            // 第四阶段：把 reaching-definition 命中转换成 DataFlow 候选边。
            var candidates = new DataFlowCandidateCollector();
            var definitionFactIndex = new DefinitionFactIndex(definitionFactsByNode);
            foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
            {
                var operationOrdinal = flowNodeOrdinals[operationNodePair.Second];
                var reachingOffset = operationOrdinal * wordsPerSet;
                if (IsBitSetEmpty(inSets, reachingOffset, wordsPerSet))
                {
                    continue;
                }

                foreach (var usedFact in plan.UsedFactsByOperation[operationNodePair.First].EnumerateFacts())
                {
                    if (definitionFactIndex.TryGetCandidates(
                      usedFact,
                      inSets,
                      reachingOffset,
                      wordsPerSet,
                      flowNodeOrdinals,
                      out var indexedCandidates))
                    {
                        foreach (var reachingDefinitionNode in indexedCandidates)
                        {
                            if (definitionFactsByNode.TryGetValue(reachingDefinitionNode, out var reachingFact) &&
                                FactsMatch(reachingFact, usedFact) &&
                                !candidates.TryAdd(reachingDefinitionNode, operationNodePair.Second, options.MaxCandidateEdgesPerMethod))
                            {
                                return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                            }
                        }
                    }
                    else
                    {
                        foreach (var reachingDefinitionOrdinal in EnumerateSetBits(inSets, reachingOffset, flowNodes.Length, wordsPerSet))
                        {
                            var reachingDefinitionNode = flowNodes[reachingDefinitionOrdinal];
                            if (definitionFactsByNode.TryGetValue(reachingDefinitionNode, out var reachingFact) &&
                                FactsMatch(reachingFact, usedFact) &&
                                !candidates.TryAdd(reachingDefinitionNode, operationNodePair.Second, options.MaxCandidateEdgesPerMethod))
                            {
                                return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                            }
                        }
                    }
                }
            }
            // 第五阶段：补显式值源流，让赋值/调用/return 的值来源可以直接连通。
            foreach (var operation in plan.OrderedOperations)
            {
                foreach (var sourceOperation in ValueSourceOperations(operation))
                {
                    if (!ReferenceEquals(sourceOperation, operation) &&
                        plan.OperationNodesByOperation.TryGetValue(sourceOperation, out var sourceNode) &&
                        plan.OperationNodesByOperation.TryGetValue(operation, out var targetNode))
                    {
                        if (!candidates.TryAdd(sourceNode, targetNode, options.MaxCandidateEdgesPerMethod))
                        {
                            return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                        }
                    }
                }
            }
            // 第六阶段：把显式 return 的值流到 MethodReturn / MethodExit。
            if (plan.ReturnNode is not null && plan.ExitNode is not null)
            {
                foreach (var returnOperation in plan.OrderedOperations.OfType<IReturnOperation>().Where(operation => operation.ReturnedValue is not null))
                {
                    if (plan.OperationNodesByOperation.TryGetValue(returnOperation.ReturnedValue!, out var valueNode) &&
                        plan.OperationNodesByOperation.TryGetValue(returnOperation, out var returnOperationNode))
                    {
                        if (!candidates.TryAdd(valueNode, plan.ReturnNode, options.MaxCandidateEdgesPerMethod) ||
                            !candidates.TryAdd(plan.ReturnNode, plan.ExitNode, options.MaxCandidateEdgesPerMethod) ||
                            !candidates.TryAdd(returnOperationNode, plan.ExitNode, options.MaxCandidateEdgesPerMethod))
                        {
                            return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                        }
                    }
                }
            }
            // 第七阶段：没有显式 return 时，为末尾可顺序流出的语句补隐式返回值路径。
            if (plan.ReturnNode is not null && plan.OrderedOperations.FirstOrDefault() is IBlockOperation methodBlock)
            {
                var terminalOperation = methodBlock.Operations.LastOrDefault();
                if (terminalOperation is not null && !ContainsExplicitReturn(methodBlock) && !StopsSequentialFlow(terminalOperation) &&
                    plan.OperationNodesByOperation.TryGetValue(terminalOperation, out var terminalNode))
                {
                    if (!candidates.TryAdd(terminalNode, plan.ReturnNode, options.MaxCandidateEdgesPerMethod))
                    {
                        return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                    }
                }
            }

            // 正常路径返回完整候选集；真正写图留给 ordered commit 阶段处理。
            return new CfgSensitivePartition(
              plan.Order,
              candidates.ToCandidateSet(),
              NLCPGDataFlowOverflowReason.None,
              CreateDataFlowMetrics(
                plan,
                wordsPerSet,
                definitionFactsByNode.Count,
                worklistIterations,
                candidates,
                NLCPGDataFlowOverflowReason.None));
        }

        private static NLCPGDataFlowMethodMetrics CreateDataFlowMetrics(
          MethodDataFlowPlan plan,
          int wordsPerSet,
          int definitionCount,
          int worklistIterations,
          DataFlowCandidateCollector? candidates,
          NLCPGDataFlowOverflowReason overflowReason)
        {
            return new NLCPGDataFlowMethodMetrics(
              plan.MethodFullName,
              plan.FlowNodes.Length,
              wordsPerSet,
              definitionCount,
              worklistIterations,
              candidates?.RawCandidateCount ?? 0,
              candidates?.UniqueCandidateCount ?? 0,
              overflowReason);
        }

        private static CfgSensitivePartition CreateCandidateLimitExceededPartition(
          MethodDataFlowPlan plan,
          NLCPGDataFlowOptions options,
          DataFlowCandidateCollector candidates,
          int definitionCount,
          int wordsPerSet,
          int worklistIterations)
        {
            ThrowIfBudgetFailure(
              options,
              plan.MethodFullName,
              NLCPGDataFlowOverflowReason.CandidateEdgeLimitExceeded);
            return new CfgSensitivePartition(
              plan.Order,
              new LocalFlowCandidateSet(Array.Empty<CpgEdgeCandidate>()),
              NLCPGDataFlowOverflowReason.CandidateEdgeLimitExceeded,
              CreateDataFlowMetrics(
                plan,
                wordsPerSet,
                definitionCount,
                worklistIterations,
                candidates,
                NLCPGDataFlowOverflowReason.CandidateEdgeLimitExceeded));
        }

        private static void ThrowIfBudgetFailure(NLCPGDataFlowOptions options, string methodFullName, NLCPGDataFlowOverflowReason overflowReason)
        {
            if (options.OverflowBehavior != NLCPGDataFlowOverflowBehavior.FailBuild)
            {
                return;
            }

            throw new InvalidOperationException(
              $"Data-flow {overflowReason} budget exceeded for {methodFullName}.");
        }

        // 把可变邻接列表冻结成数组，避免求解阶段再暴露可写集合。
        private static Dictionary<NLCPGNode, NLCPGNode[]> SnapshotNeighbors(IReadOnlyDictionary<NLCPGNode, List<NLCPGNode>> neighbors)
        {
            return neighbors.ToDictionary(
              pair => pair.Key,
              pair => pair.Value.ToArray());
        }

        private static void AddUniqueFlowNode(List<NLCPGNode> flowNodes, HashSet<NLCPGNode> flowNodeSet, NLCPGNode node)
        {
            if (flowNodeSet.Add(node))
            {
                flowNodes.Add(node);
            }
        }

        // 从预先缓存的 CFG 邻接中裁出“当前方法实际关心的节点子图”。
        private Dictionary<NLCPGNode, List<NLCPGNode>> BuildFlowNeighborsFromCache(HashSet<NLCPGNode> flowNodes, bool incoming)
        {
            var neighbors = flowNodes.ToDictionary(node => node, _ => new List<NLCPGNode>());
            foreach (var node in flowNodes)
            {
                var cachedNeighbors = incoming
                  ? GetCachedCfgPredecessors(node)
                  : GetCachedCfgSuccessors(node);
                if (cachedNeighbors.Count == 0)
                {
                    continue;
                }

                foreach (var neighborNode in cachedNeighbors)
                {
                    if (flowNodes.Contains(neighborNode))
                    {
                        neighbors[node].Add(neighborNode);
                    }
                }
            }

            return neighbors;
        }

        private static int BitSetWordCount(int bitCount)
        {
            return Math.Max(1, (bitCount + 63) / 64);
        }

        private static bool IsBitSet(ulong[] bitSet, int offset, int ordinal)
        {
            return (bitSet[offset + (ordinal / 64)] & (1UL << (ordinal % 64))) != 0;
        }

        private static void OrBitSet(ulong[] target, ulong[] source, int sourceOffset, int wordsPerSet)
        {
            for (var wordIndex = 0; wordIndex < wordsPerSet; wordIndex += 1)
            {
                target[wordIndex] |= source[sourceOffset + wordIndex];
            }
        }

        private static void CopyBitSet(ulong[] target, int targetOffset, ulong[] source, int wordsPerSet)
        {
            Array.Copy(source, 0, target, targetOffset, wordsPerSet);
        }

        private static bool BitSetEquals(ulong[] left, int leftOffset, ulong[] right, int wordsPerSet)
        {
            for (var wordIndex = 0; wordIndex < wordsPerSet; wordIndex += 1)
            {
                if (left[leftOffset + wordIndex] != right[wordIndex])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBitSetEmpty(ulong[] bitSet, int offset, int wordsPerSet)
        {
            for (var wordIndex = 0; wordIndex < wordsPerSet; wordIndex += 1)
            {
                if (bitSet[offset + wordIndex] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<int> EnumerateSetBits(ulong[] bitSet, int offset, int bitCount, int wordsPerSet)
        {
            for (var wordIndex = 0; wordIndex < wordsPerSet; wordIndex += 1)
            {
                var word = bitSet[offset + wordIndex];
                while (word != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(word);
                    var ordinal = (wordIndex * 64) + bitIndex;
                    if (ordinal < bitCount)
                    {
                        yield return ordinal;
                    }

                    word &= word - 1;
                }
            }
        }

        private static void ApplyDefinitionTransfer(ulong[] output, int nodeOrdinal, int wordsPerSet, DefinitionFact definedFact, IReadOnlyList<DefinitionFact?> definitionFactsByOrdinal)
        {
            for (var wordIndex = 0; wordIndex < wordsPerSet; wordIndex += 1)
            {
                var word = output[wordIndex];
                while (word != 0)
                {
                    var bitIndex = BitOperations.TrailingZeroCount(word);
                    var definitionOrdinal = (wordIndex * 64) + bitIndex;
                    if (definitionOrdinal < definitionFactsByOrdinal.Count &&
                        definitionFactsByOrdinal[definitionOrdinal] is { } priorFact &&
                        FactsConflict(priorFact, definedFact))
                    {
                        output[wordIndex] &= ~(1UL << bitIndex);
                    }

                    word &= word - 1;
                }
            }

            output[nodeOrdinal / 64] |= 1UL << (nodeOrdinal % 64);
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

        private void AddCallArgumentAndReturnDataFlow(NLCPGBuildContext context)
        {
            var graph = context.Graph;
            foreach (var invocation in context.InvocationOperations)
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

                if (!_resolvedCallTargetsByInvocation.TryGetValue(invocation, out var effectiveTargets))
                {
                    throw new InvalidOperationException("Data-flow requires CallGraphPass to cache resolved call targets.");
                }

                foreach (var candidateMethod in effectiveTargets.Where(IsInternalMethod).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
                {
                    AddArgumentToParameterFlows(invocation, candidateMethod, graph);

                    var returnNode = GetOrCreateMethodReturnNode(candidateMethod, graph);
                    graph.AddEdge(returnNode, callSiteNode, NLCPGEdgeKind.DataFlow);
                }
            }

            foreach (var propertyReference in context.PropertyReferenceOperations)
            {
                AddPropertyAccessorSummaryDataFlow(propertyReference, graph);
            }
        }

        private void AddPropertyAccessorSummaryDataFlow(IPropertyReferenceOperation propertyReference, NLCPGGraph graph)
        {
            AddPropertyGetterSummaryDataFlow(propertyReference, graph);
            AddPropertySetterSummaryDataFlow(propertyReference, graph);
        }

        private void AddPropertyGetterSummaryDataFlow(IPropertyReferenceOperation propertyReference, NLCPGGraph graph)
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

            // 属性访问既是语义节点，也是 accessor callsite 的宿主，需要两条路径都连通。
            var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
            var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, getter, graph);
            AddPropertyAccessorParameterFlows(propertyReference, getter, callSiteNode, includesSetterValue: false, setterValue: null, graph);

            var returnNode = GetOrCreateMethodReturnNode(getter, graph);
            graph.AddEdge(returnNode, propertyNode, NLCPGEdgeKind.DataFlow);
            if (callSiteNode is not null)
            {
                graph.AddEdge(returnNode, callSiteNode, NLCPGEdgeKind.DataFlow);
                graph.AddEdge(callSiteNode, propertyNode, NLCPGEdgeKind.DataFlow);
            }
        }

        private void AddPropertySetterSummaryDataFlow(IPropertyReferenceOperation propertyReference, NLCPGGraph graph)
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

            // setter 的 value 既流向 accessor 参数，也直接流向属性访问节点本身。
            var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
            var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, setter, graph);
            AddPropertyAccessorParameterFlows(propertyReference, setter, callSiteNode, includesSetterValue: true, assignment.Value, graph);

            graph.AddEdge(GetOrCreateOperationNode(assignment.Value, graph), propertyNode, NLCPGEdgeKind.DataFlow);
            if (callSiteNode is not null)
            {
                graph.AddEdge(callSiteNode, propertyNode, NLCPGEdgeKind.DataFlow);
            }
        }

        private void AddPropertyAccessorParameterFlows(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod, NLCPGNode? callSiteNode, bool includesSetterValue, IOperation? setterValue, NLCPGGraph graph)
        {
            var parameterIndex = 0;
            if (propertyReference.Instance is not null && accessorMethod.Parameters.Length > parameterIndex)
            {
                var receiverNode = GetOrCreateOperationNode(propertyReference.Instance, graph);
                var receiverParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
                graph.AddEdge(receiverNode, receiverParameterNode, NLCPGEdgeKind.ParameterLink);
                graph.AddEdge(receiverNode, receiverParameterNode, NLCPGEdgeKind.DataFlow);
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
                graph.AddEdge(argumentNode, argumentParameterNode, NLCPGEdgeKind.ParameterLink);
                graph.AddEdge(argumentNode, argumentParameterNode, NLCPGEdgeKind.DataFlow);
                parameterIndex += 1;
            }

            if (includesSetterValue &&
                setterValue is not null &&
                parameterIndex < accessorMethod.Parameters.Length)
            {
                var valueNode = GetOrCreateOperationNode(setterValue, graph);
                var setterValueParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
                graph.AddEdge(valueNode, setterValueParameterNode, NLCPGEdgeKind.ParameterLink);
                graph.AddEdge(valueNode, setterValueParameterNode, NLCPGEdgeKind.DataFlow);
                if (callSiteNode is not null)
                {
                    graph.AddEdge(valueNode, callSiteNode, NLCPGEdgeKind.DataFlow);
                }
            }
        }

        private void AddArgumentToParameterFlows(IInvocationOperation invocation, IMethodSymbol candidateMethod, NLCPGGraph graph)
        {
            var parameterIndex = 0;

            // 扩展方法的 receiver 会被映射到第一个形参，因此要先单独处理。
            if (candidateMethod.IsExtensionMethod && invocation.Instance is not null && candidateMethod.Parameters.Length > 0)
            {
                var receiverNode = GetOrCreateOperationNode(invocation.Instance, graph);
                var receiverParameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[0], graph);
                graph.AddEdge(receiverNode, receiverParameterNode, NLCPGEdgeKind.ParameterLink);
                graph.AddEdge(receiverNode, receiverParameterNode, NLCPGEdgeKind.DataFlow);
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
                graph.AddEdge(argumentNode, parameterNode, NLCPGEdgeKind.ParameterLink);
                graph.AddEdge(argumentNode, parameterNode, NLCPGEdgeKind.DataFlow);
            }
        }

        private NLCPGNode? FindCallSiteNode(IInvocationOperation invocationOperation, NLCPGGraph graph)
        {
            return _callSiteNodesByInvocation.TryGetValue(invocationOperation, out var callSiteNode)
              ? callSiteNode
              : null;
        }

        private NLCPGNode? FindPropertyAccessorCallSiteNode(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod, NLCPGGraph graph)
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
