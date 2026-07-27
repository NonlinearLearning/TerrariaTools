using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Diagnostics;

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
            public int ReleasedCfgSensitivePlanCount { get; set; }
            public long PrepareFlowNodesElapsedTicks { get; set; }
            public long CollectUsedFactsElapsedTicks { get; set; }
        }

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
            long EnumerateOrderedOperationsMilliseconds,
            long CollectUsedFactsMilliseconds,
            long CollectUsedFactsTicks,
            int UsedFactCount);

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
          NLCPGDataFlowOverflowReason OverflowReason);

        internal void RunDataFlowPass(NLCPGBuildContext context)
        {
            AddReachingDefinitionDataFlow(context, new DataFlowPassMetrics());
        }

        private void AddReachingDefinitionDataFlow(NLCPGBuildContext context, DataFlowPassMetrics metrics)
        {
            var graph = context.Graph;
            var enumerateMethodBlocksStopwatch = Stopwatch.StartNew();
            var methodBlocks = new List<IBlockOperation>();
            var owningMethods = new Dictionary<IOperation, IMethodSymbol>(ReferenceEqualityComparer.Instance);
            // 只抽出真正的方法根 block，避免局部 block 被当成独立数据流单元。
            foreach (var rootEntry in context.OperationInventory)
            {
                if (!rootEntry.IsRoot || rootEntry.Operation is not IBlockOperation methodBlock || !IsMethodRootBlock(methodBlock))
                {
                    continue;
                }

                methodBlocks.Add(methodBlock);
                if (rootEntry.OwningMethod is IMethodSymbol methodSymbol)
                {
                    owningMethods[methodBlock] = methodSymbol;
                }
            }
            enumerateMethodBlocksStopwatch.Stop();
            metrics.EnumerateMethodBlocksElapsedMilliseconds = enumerateMethodBlocksStopwatch.ElapsedMilliseconds;
            metrics.MethodBlockCount = methodBlocks.Count;

            var prepareFlowNodesStopwatch = Stopwatch.StartNew();
            var operationIndex = CreateDataFlowOperationIndex(methodBlocks, owningMethods, graph);
            // use-fact 采集可以并行做；后续 CFG 敏感求解依赖它的冻结结果。
            var usedFactPartitions = RunUsedFactPartitionsAsync(methodBlocks).GetAwaiter().GetResult();
            metrics.UsedFactPartitionCount = usedFactPartitions.Count;
            metrics.UsedFactPartitionMaxDegreeOfParallelism = _options.EffectiveMaxDegreeOfParallelism;
            var cfgSensitivePlans = BuildCfgSensitivePartitionPlans(usedFactPartitions, methodBlocks, operationIndex, graph, metrics);
            prepareFlowNodesStopwatch.Stop();
            metrics.PrepareFlowNodesElapsedMilliseconds = prepareFlowNodesStopwatch.ElapsedMilliseconds;
            metrics.PrepareFlowNodesElapsedTicks = prepareFlowNodesStopwatch.ElapsedTicks;
            metrics.FrozenOperationNodeCount = operationIndex.NodesByOperation.Count;
            // CFG-sensitive 分区按 order 提交，保持图提交顺序稳定。
            RunCfgSensitivePartitionsInOrder(cfgSensitivePlans,graph,metrics);
            metrics.CfgSensitivePartitionCount = cfgSensitivePlans.Count;
            metrics.CfgSensitivePartitionMaxDegreeOfParallelism = _options.EffectiveMaxDegreeOfParallelism;

            foreach (var partition in usedFactPartitions.OrderBy(partition => partition.Order))
            {
                var orderedOperations = partition.OrderedOperations;
                metrics.EnumerateOrderedOperationsElapsedMilliseconds += partition.EnumerateOrderedOperationsMilliseconds;
                metrics.OrderedOperationCount += orderedOperations.Length;
                metrics.CollectUsedFactsElapsedMilliseconds += partition.CollectUsedFactsMilliseconds;
                metrics.CollectUsedFactsElapsedTicks += partition.CollectUsedFactsTicks;
                metrics.UsedFactCount += partition.UsedFactCount;
                metrics.UsedFactRecordCount += partition.OrderedOperations.Length;
                metrics.MethodOperationNodeProjectionCount += partition.OrderedOperations.Length;

            }

            var callArgumentAndReturnStopwatch = Stopwatch.StartNew();
            // 方法内数据流补完后，再单独处理跨调用/属性访问的摘要流。
            AddCallArgumentAndReturnDataFlow(context);
            callArgumentAndReturnStopwatch.Stop();
            metrics.CallArgumentAndReturnElapsedMilliseconds += callArgumentAndReturnStopwatch.ElapsedMilliseconds;
        }

        private async Task<IReadOnlyList<UsedFactPartition>> RunUsedFactPartitionsAsync(IReadOnlyList<IBlockOperation> methodBlocks)
        {
            return await BoundedPartitionWorkWindow.RunAsync(
              methodBlocks,
              _options.EffectiveMaxDegreeOfParallelism,
              AnalyzeUsedFactPartition);
        }

        private static UsedFactPartition AnalyzeUsedFactPartition(IBlockOperation methodBlock, int order)
        {
            var orderedOperationsStopwatch = Stopwatch.StartNew();
            var orderedOperations = methodBlock.DescendantsAndSelf().ToArray();
            orderedOperationsStopwatch.Stop();

            var usedFactsStopwatch = Stopwatch.StartNew();
            var usedFactsByOperation = new Dictionary<IOperation, UsedFactRecord>(
              ReferenceEqualityComparer.Instance);
            var usedFactCount = 0;
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

        private IReadOnlyList<MethodDataFlowPlan> BuildCfgSensitivePartitionPlans(IReadOnlyList<UsedFactPartition> usedFactPartitions, IReadOnlyList<IBlockOperation> methodBlocks, DataFlowOperationIndex operationIndex, NLCPGGraph graph, DataFlowPassMetrics metrics)
        {
            var plans = new List<MethodDataFlowPlan>(usedFactPartitions.Count);
            foreach (var partition in usedFactPartitions.OrderBy(partition => partition.Order))
            {
                var methodBlock = methodBlocks[partition.Order];
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
                if (operationIndex.OwningMethods.TryGetValue(methodBlock, out var methodSymbol))
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
                if (operationIndex.OwningMethods.TryGetValue(methodBlock, out var flowMethodSymbol))
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

                var flowNeighborsStopwatch = Stopwatch.StartNew();
                var predecessors = SnapshotNeighbors(BuildFlowNeighborsFromCache(flowNodeSet, incoming: true));
                var successors = SnapshotNeighbors(BuildFlowNeighborsFromCache(flowNodeSet, incoming: false));
                flowNeighborsStopwatch.Stop();
                metrics.BuildFlowNeighborsElapsedMilliseconds += flowNeighborsStopwatch.ElapsedMilliseconds;
                plans.Add(new MethodDataFlowPlan(
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
                  successors));
            }

            return plans;
        }

        private void RunCfgSensitivePartitionsInOrder(IReadOnlyList<MethodDataFlowPlan> plans, NLCPGGraph graph, DataFlowPassMetrics metrics)
        {
            BoundedPartitionWorkWindow.RunOrdered(
              plans,
              _options.EffectiveMaxDegreeOfParallelism,
              (plan, _) => AnalyzeCfgSensitivePartition(plan, _options.EffectiveDataFlowOptions),
              (partition, order) =>
              {
                  try
                  {
                      CommitCfgSensitivePartition(plans[order], partition, graph, metrics);
                  }
                  finally
                  {
                      plans[order].Release();
                      metrics.ReleasedCfgSensitivePlanCount += 1;
                  }
              },
              retainedRecordCount: partition => partition.Candidates.EdgeCandidates.Count,
              reorderAllowance: _options.EffectiveOrderedResultReorderAllowance,
              maxCompletedRecordCount: _options.EffectiveMaxOrderedResultRecordCount);
        }

        private static void CommitCfgSensitivePartition(MethodDataFlowPlan plan, CfgSensitivePartition partition, NLCPGGraph graph, DataFlowPassMetrics metrics)
        {
            if (partition.Order != plan.Order)
            {
                throw new InvalidOperationException("Candidate batch order does not match its frozen plan.");
            }

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

            if (partition.OverflowReason != NLCPGDataFlowOverflowReason.None)
            {
                metrics.SkippedMethodCount += 1;
                return;
            }

            // 真正的图提交只发生在这里，保证并行求解阶段不修改共享图。
            var commitStopwatch = Stopwatch.StartNew();
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
            commitStopwatch.Stop();

            metrics.CfgSensitiveCandidateCommitElapsedMilliseconds += commitStopwatch.ElapsedMilliseconds;
        }

        private static CfgSensitivePartition AnalyzeCfgSensitivePartition(MethodDataFlowPlan plan, NLCPGDataFlowOptions options)
        {
            var totalStopwatch = Stopwatch.StartNew();
            var definitionFactsByNode = new Dictionary<NLCPGNode, DefinitionFact>(
              plan.ParameterDefinitionFacts);

            // 第一阶段：把“哪些节点定义了什么”整理成节点索引。
            var createDefinitionFactsStopwatch = Stopwatch.StartNew();
            foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
            {
                var definedFact = DefinedFact(operationNodePair.First);
                if (definedFact is not null)
                {
                    definitionFactsByNode[operationNodePair.Second] = definedFact;
                }
            }
            createDefinitionFactsStopwatch.Stop();

            var unreachableNodeCount = CountUnreachableNodes(plan);
            // 预算先于 fixpoint 检查，超限时直接按策略跳过或失败。
            if (definitionFactsByNode.Count > options.MaxDefinitionsPerMethod)
            {
                ThrowIfBudgetFailure(
                  options,
                  plan.MethodFullName,
                  NLCPGDataFlowOverflowReason.DefinitionLimitExceeded);
                totalStopwatch.Stop();
                return new CfgSensitivePartition(
                  plan.Order,
                  new LocalFlowCandidateSet(Array.Empty<CpgEdgeCandidate>()),
                  totalStopwatch.ElapsedMilliseconds,
                  createDefinitionFactsStopwatch.ElapsedMilliseconds,
                  InitializeStateMilliseconds: 0,
                  FixpointMilliseconds: 0,
                  ReachingDefinitionEdgeMilliseconds: 0,
                  ValueSourceEdgeMilliseconds: 0,
                  ReturnFlowEdgeMilliseconds: 0,
                  TerminalFlowEdgeMilliseconds: 0,
                  plan.FlowNodes.Length,
                  definitionFactsByNode.Count,
                  FixpointIterations: 0,
                  UnreachableNodeCount: unreachableNodeCount,
                  GeneratedCandidateCount: 0,
                  OverflowReason: NLCPGDataFlowOverflowReason.DefinitionLimitExceeded);
            }

            if (plan.FlowNodes.Length > options.MaxFlowNodesPerMethod)
            {
                ThrowIfBudgetFailure(
                  options,
                  plan.MethodFullName,
                  NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded);
                totalStopwatch.Stop();
                return new CfgSensitivePartition(
                  plan.Order,
                  new LocalFlowCandidateSet(Array.Empty<CpgEdgeCandidate>()),
                  totalStopwatch.ElapsedMilliseconds,
                  createDefinitionFactsStopwatch.ElapsedMilliseconds,
                  InitializeStateMilliseconds: 0,
                  FixpointMilliseconds: 0,
                  ReachingDefinitionEdgeMilliseconds: 0,
                  ValueSourceEdgeMilliseconds: 0,
                  ReturnFlowEdgeMilliseconds: 0,
                  TerminalFlowEdgeMilliseconds: 0,
                  plan.FlowNodes.Length,
                  definitionFactsByNode.Count,
                  FixpointIterations: 0,
                  UnreachableNodeCount: unreachableNodeCount,
                  GeneratedCandidateCount: 0,
                  OverflowReason: NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded);
            }

            // 第二阶段：初始化 in/out 集和工作队列，准备跑数据流不动点。
            var initializeStateStopwatch = Stopwatch.StartNew();
            var flowNodes = plan.FlowNodes;
            var inSets = flowNodes.ToDictionary(
              node => node,
              _ => new HashSet<NLCPGNode>());
            var outSets = flowNodes.ToDictionary(
              node => node,
              _ => new HashSet<NLCPGNode>());
            var worklist = new Queue<NLCPGNode>(flowNodes);
            var queued = new HashSet<NLCPGNode>(flowNodes);
            initializeStateStopwatch.Stop();

            // 第三阶段：标准 worklist fixpoint，按 predecessor/out 集传播 reaching definitions。
            var fixpointStopwatch = Stopwatch.StartNew();
            var fixpointIterations = 0;
            while (worklist.Count > 0)
            {
                fixpointIterations += 1;
                var nodeId = worklist.Dequeue();
                queued.Remove(nodeId);
                var incomingDefinitions = new HashSet<NLCPGNode>();
                foreach (var predecessorNode in plan.Predecessors[nodeId])
                {
                    incomingDefinitions.UnionWith(outSets[predecessorNode]);
                }

                inSets[nodeId] = incomingDefinitions;
                var updatedOut = ApplyDefinitionTransfer(nodeId, incomingDefinitions, definitionFactsByNode);
                if (updatedOut.SetEquals(outSets[nodeId]))
                {
                    continue;
                }

                outSets[nodeId] = updatedOut;
                foreach (var successorNode in plan.Successors[nodeId])
                {
                    if (queued.Add(successorNode))
                    {
                        worklist.Enqueue(successorNode);
                    }
                }
            }
            fixpointStopwatch.Stop();

            // 第四阶段：把 reaching-definition 命中转换成 DataFlow 候选边。
            var edgeStopwatch = Stopwatch.StartNew();
            var edges = new List<CpgEdgeCandidate>();
            foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
            {
                foreach (var usedFact in plan.UsedFactsByOperation[operationNodePair.First].EnumerateFacts())
                {
                    foreach (var reachingDefinitionNode in inSets[operationNodePair.Second])
                    {
                        if (definitionFactsByNode.TryGetValue(reachingDefinitionNode, out var reachingFact) &&
                            FactsMatch(reachingFact, usedFact))
                        {
                            edges.Add(CreateDataFlowCandidate(reachingDefinitionNode, operationNodePair.Second));
                        }
                    }
                }
            }
            edgeStopwatch.Stop();
            // 第五阶段：补显式值源流，让赋值/调用/return 的值来源可以直接连通。
            var valueSourceStopwatch = Stopwatch.StartNew();
            foreach (var operation in plan.OrderedOperations)
            {
                foreach (var sourceOperation in ValueSourceOperations(operation))
                {
                    if (!ReferenceEquals(sourceOperation, operation) &&
                        plan.OperationNodesByOperation.TryGetValue(sourceOperation, out var sourceNode) &&
                        plan.OperationNodesByOperation.TryGetValue(operation, out var targetNode))
                    {
                        edges.Add(CreateDataFlowCandidate(sourceNode, targetNode));
                    }
                }
            }
            valueSourceStopwatch.Stop();
            // 第六阶段：把显式 return 的值流到 MethodReturn / MethodExit。
            var returnFlowStopwatch = Stopwatch.StartNew();
            if (plan.ReturnNode is not null && plan.ExitNode is not null)
            {
                foreach (var returnOperation in plan.OrderedOperations.OfType<IReturnOperation>().Where(operation => operation.ReturnedValue is not null))
                {
                    if (plan.OperationNodesByOperation.TryGetValue(returnOperation.ReturnedValue!, out var valueNode) &&
                        plan.OperationNodesByOperation.TryGetValue(returnOperation, out var returnOperationNode))
                    {
                        edges.Add(CreateDataFlowCandidate(valueNode, plan.ReturnNode));
                        edges.Add(CreateDataFlowCandidate(plan.ReturnNode, plan.ExitNode));
                        edges.Add(CreateDataFlowCandidate(returnOperationNode, plan.ExitNode));
                    }
                }
            }
            returnFlowStopwatch.Stop();
            // 第七阶段：没有显式 return 时，为末尾可顺序流出的语句补隐式返回值路径。
            var terminalFlowStopwatch = Stopwatch.StartNew();
            if (plan.ReturnNode is not null && plan.OrderedOperations.FirstOrDefault() is IBlockOperation methodBlock)
            {
                var terminalOperation = methodBlock.Operations.LastOrDefault();
                if (terminalOperation is not null && !ContainsExplicitReturn(methodBlock) && !StopsSequentialFlow(terminalOperation) &&
                    plan.OperationNodesByOperation.TryGetValue(terminalOperation, out var terminalNode))
                {
                    edges.Add(CreateDataFlowCandidate(terminalNode, plan.ReturnNode));
                }
            }
            terminalFlowStopwatch.Stop();
            if (edges.Count > options.MaxCandidateEdgesPerMethod)
            {
                ThrowIfBudgetFailure(
                  options,
                  plan.MethodFullName,
                  NLCPGDataFlowOverflowReason.CandidateEdgeLimitExceeded);

                totalStopwatch.Stop();
                return new CfgSensitivePartition(
                  plan.Order,
                  new LocalFlowCandidateSet(Array.Empty<CpgEdgeCandidate>()),
                  totalStopwatch.ElapsedMilliseconds,
                  createDefinitionFactsStopwatch.ElapsedMilliseconds,
                  initializeStateStopwatch.ElapsedMilliseconds,
                  fixpointStopwatch.ElapsedMilliseconds,
                  edgeStopwatch.ElapsedMilliseconds,
                  valueSourceStopwatch.ElapsedMilliseconds,
                  returnFlowStopwatch.ElapsedMilliseconds,
                  terminalFlowStopwatch.ElapsedMilliseconds,
                  plan.FlowNodes.Length,
                  definitionFactsByNode.Count,
                  fixpointIterations,
                  unreachableNodeCount,
                  edges.Count,
                  NLCPGDataFlowOverflowReason.CandidateEdgeLimitExceeded);
            }
            totalStopwatch.Stop();

            // 正常路径返回完整候选集；真正写图留给 ordered commit 阶段处理。
            return new CfgSensitivePartition(
              plan.Order,
              new LocalFlowCandidateSet(edges),
              totalStopwatch.ElapsedMilliseconds,
              createDefinitionFactsStopwatch.ElapsedMilliseconds,
              initializeStateStopwatch.ElapsedMilliseconds,
              fixpointStopwatch.ElapsedMilliseconds,
              edgeStopwatch.ElapsedMilliseconds,
              valueSourceStopwatch.ElapsedMilliseconds,
              returnFlowStopwatch.ElapsedMilliseconds,
              terminalFlowStopwatch.ElapsedMilliseconds,
              plan.FlowNodes.Length,
              definitionFactsByNode.Count,
              fixpointIterations,
              unreachableNodeCount,
              edges.Count,
              NLCPGDataFlowOverflowReason.None);
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

        private static int CountUnreachableNodes(MethodDataFlowPlan plan)
        {
            var visited = new HashSet<NLCPGNode>();
            var worklist = new Queue<NLCPGNode>(plan.FlowNodes
              .Where(node => plan.Predecessors[node].Length == 0));
            while (worklist.Count > 0)
            {
                var nodeId = worklist.Dequeue();
                if (!visited.Add(nodeId))
                {
                    continue;
                }

                foreach (var successorNode in plan.Successors[nodeId])
                {
                    if (!visited.Contains(successorNode))
                    {
                        worklist.Enqueue(successorNode);
                    }
                }
            }

            return plan.FlowNodes.Length - visited.Count;
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

        private static HashSet<NLCPGNode> ApplyDefinitionTransfer(NLCPGNode node, HashSet<NLCPGNode> incomingDefinitions, Dictionary<NLCPGNode, DefinitionFact> definitionFactsByNode)
        {
            var outgoingDefinitions = new HashSet<NLCPGNode>(incomingDefinitions);
            if (!definitionFactsByNode.TryGetValue(node, out var definedFact))
            {
                return outgoingDefinitions;
            }

            outgoingDefinitions.RemoveWhere(definitionNode =>
              definitionFactsByNode.TryGetValue(definitionNode, out var priorFact) &&
              FactsConflict(priorFact, definedFact));
            outgoingDefinitions.Add(node);
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

        private void AddCallArgumentAndReturnDataFlow(NLCPGBuildContext context)
        {
            var graph = context.Graph;
            foreach (var invocation in EnumerateOperations(context).OfType<IInvocationOperation>())
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

                // 先按现有调用解析/排序逻辑收窄目标，再只对内部方法补摘要边。
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
                    graph.AddEdge(returnNode, callSiteNode, NLCPGEdgeKind.DataFlow);
                }
            }

            foreach (var propertyReference in EnumerateOperations(context).OfType<IPropertyReferenceOperation>())
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
