using System.Diagnostics;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Concurrency;
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
            internal MethodDataFlowPlan(int order, string methodFullName, IOperation[] orderedOperations, NLCPGNode[] flowNodes, NLCPGNode[] operationNodes, Dictionary<IOperation, UsedFactRecord> usedFactsByOperation, Dictionary<NLCPGNode, DefinitionFact> parameterDefinitionFacts, Dictionary<IOperation, NLCPGNode> operationNodesByOperation, NLCPGNode? returnNode, NLCPGNode? exitNode, Dictionary<NLCPGNode, int> flowNodeOrdinals, int[] predecessorOffsets, int[] predecessorOrdinals, int[] successorOffsets, int[] successorOrdinals)
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
                FlowNodeOrdinals = flowNodeOrdinals;
                PredecessorOffsets = predecessorOffsets;
                PredecessorOrdinals = predecessorOrdinals;
                SuccessorOffsets = successorOffsets;
                SuccessorOrdinals = successorOrdinals;
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

            // 方法局部序号邻接（CSR）。flow 节点序号既是 Fixpoint 的 worklist 下标，
            // 也是这里的邻接下标，故消费循环不再需要「节点 → 序号」字典查找。
            internal Dictionary<NLCPGNode, int> FlowNodeOrdinals { get; private set; }

            // offsets 长度 N + 1；nodeOrdinal 的邻接区间为
            // ordinals[offsets[nodeOrdinal] .. offsets[nodeOrdinal + 1]]。
            // 空区间由相等 offsets 表示；零节点/零边共享 Array.Empty，不逐节点建数组或包装对象。
            internal int[] PredecessorOffsets { get; private set; }
            internal int[] PredecessorOrdinals { get; private set; }
            internal int[] SuccessorOffsets { get; private set; }
            internal int[] SuccessorOrdinals { get; private set; }

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
                FlowNodeOrdinals = new Dictionary<NLCPGNode, int>();
                PredecessorOffsets = Array.Empty<int>();
                PredecessorOrdinals = Array.Empty<int>();
                SuccessorOffsets = Array.Empty<int>();
                SuccessorOrdinals = Array.Empty<int>();
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
            private readonly DataFlowMethodProbe? _detail;
            private readonly List<CpgEdgeCandidate> _uniqueCandidates = new();
            private readonly HashSet<CpgEdgeCandidate> _seenCandidates = new();

            internal DataFlowCandidateCollector(DataFlowMethodProbe? detail)
            {
                _detail = detail;
            }

            internal int RawCandidateCount { get; private set; }

            internal int UniqueCandidateCount => _uniqueCandidates.Count;

            internal bool TryAdd(NLCPGNode sourceNode, NLCPGNode targetNode, int maxCandidateEdges,
                bool? fallback = null)
            {
                if (fallback.HasValue)
                {
                    _detail?.Count(fallback.Value ? DataFlowCounter.FallbackCollectorCalls
                        : DataFlowCounter.IndexedCollectorCalls);
                }
                bool timed = _detail?.Phase == DataFlowPhase.CandidateLoop;
                long start = timed ? Stopwatch.GetTimestamp() : 0;
                try
                {
                    return TryAddCore(sourceNode, targetNode, maxCandidateEdges);
                }
                finally
                {
                    if (timed)
                    {
                        _detail!.Elapsed(DataFlowDetail.CandidateCollect, start);
                    }
                }
            }

            private bool TryAddCore(NLCPGNode sourceNode, NLCPGNode targetNode, int maxCandidateEdges)
            {
                RawCandidateCount += 1;
                CountCandidate(added: false);
                if (RawCandidateCount > maxCandidateEdges)
                {
                    _detail?.Count(DataFlowCounter.CandidateBudgetRejects);
                    return false;
                }

                var candidate = CreateDataFlowCandidate(sourceNode, targetNode);
                if (_seenCandidates.Add(candidate))
                {
                    _uniqueCandidates.Add(candidate);
                    CountCandidate(added: true);
                }
                else
                {
                    _detail?.Count(DataFlowCounter.DuplicateCandidates);
                }

                return true;
            }

            private void CountCandidate(bool added)
            {
                if (_detail is null)
                {
                    return;
                }
                _detail.Count(added ? DataFlowCounter.UniqueCandidates : DataFlowCounter.RawCandidates);
                var counter = _detail.Phase switch
                {
                    DataFlowPhase.ExplicitSources => added
                        ? DataFlowCounter.ExplicitUnique : DataFlowCounter.ExplicitRaw,
                    DataFlowPhase.ReturnBoundary => added
                        ? DataFlowCounter.ReturnUnique : DataFlowCounter.ReturnRaw,
                    _ => added ? DataFlowCounter.CandidateLoopUnique : DataFlowCounter.CandidateLoopRaw,
                };
                _detail.Count(counter);
            }

            internal LocalFlowCandidateSet ToCandidateSet()
            {
                return new LocalFlowCandidateSet(_uniqueCandidates);
            }
        }

        private sealed class DefinitionFactIndex
        {
            private readonly DataFlowMethodProbe? _detail;
            // 索引直接存放**紧凑定义序数**而不是 NLCPGNode。
            // 索引项本就取自 factsByNode，而定义序数正是由 factsByNode 生成的，
            // 因此“该节点是否在流节点表内/是否有定义位”这两个判断在建立索引时即可结晶，
            // 扫描期无需再做 Dictionary<NLCPGNode,int> 查找（NLCPGNode.GetHashCode 实测约 70 ns）。
            private readonly Dictionary<string, List<int>> _byLocation = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<int>> _byRoot = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<int>> _byBase = new(StringComparer.Ordinal);
            // 此处曾有一个 _byBaseAndPath（键为 $"{BaseKey}\u001F{PathKey}"）桶及其查询，已删除。
            //
            // 证明它不可能新增通过 FactsMatch 的提交。设 usedFact 的键为 (L, b, p)，
            // 被 BasePath 命中而 Location/Root 均未命中的定义 d 满足
            // b == d.BaseKey && p == d.PathKey（拼接后相等；即使存在 U+001F 分隔符歧义，
            // 命中集合也只是这个集合的超集，证明仍覆盖）。此时 d 的处理分三种：
            //
            // 1. FactsMatch 首行 LocationKey 相等：d 已在 _byLocation[L] 中命中，矛盾。
            // 2. IsContainerMatch：需 FactRootKey(d) == b，即 d.BaseKey ?? d.LocationKey == b。
            //    若 d.BaseKey == b，则 d 已在 _byRoot[b] 中命中（Root 以 BaseKey 建桶），矛盾；
            //    否则 d.BaseKey != b，而 d.BaseKey == b 由 BasePath 键相等给出，矛盾。
            // 3. IsPartMatch：需 d.BaseKey == FactRootKey(usedFact) == b ?? L。
            //    若 b 非空则 d.BaseKey == b，同 2 已在 _byRoot[b] 命中，矛盾；
            //    若 b 为空/为 null，则 BasePath 查询的前置条件（非空 BaseKey 与 PathKey）不成立，
            //    该桶根本不会被查询。
            // 4. IsAliasMatch：需 d.BaseKey == b（同 2）或走到末尾的 BaseKey+Location 相等分支。
            // 5. 末尾 BaseKey 分支：需 d.BaseKey == b，同 2。
            //
            // 因此 BasePath 的每个命中都已被 Location 或 Root 先行处理；又因为 Root 与 BasePath
            // 共享 reachingSlot 且中间不清 _seen，后扫的 BasePath 不可能追加候选。
            // 删除后唯一可观测的差异是索引内部未通过 FactsMatch 的候选清单可能变短
            // （见分隔符歧义边界测试），而图、collector 提交顺序、RawCandidateCount 与预算行为
            // 逐项不变。

            private readonly IReadOnlyList<NLCPGNode> _definitionNodes;
            // 用定义序数打标记去重，避免 HashSet<NLCPGNode> 的哈希开销；
            // 每次调用只回滚上次真正置位的少量条目，故无需整表清零。
            private readonly bool[] _seen;
            private readonly List<int> _touched = new();
            private readonly List<NLCPGNode> _matches = new();

            internal DefinitionFactIndex(
              IReadOnlyDictionary<NLCPGNode, DefinitionFact> factsByNode,
              IReadOnlyDictionary<NLCPGNode, int> flowNodeOrdinals,
              int[] definitionOrdinals,
              IReadOnlyList<NLCPGNode> definitionNodes,
              DataFlowMethodProbe? detail)
            {
                _detail = detail;
                _definitionNodes = definitionNodes;
                _seen = new bool[definitionNodes.Count];
                foreach (var pair in factsByNode)
                {
                    detail?.Count(DataFlowCounter.IndexSourceDefinitionVisits);
                    if (!flowNodeOrdinals.TryGetValue(pair.Key, out var flowOrdinal))
                    {
                        continue;
                    }

                    var definitionOrdinal = definitionOrdinals[flowOrdinal];
                    if (definitionOrdinal < 0)
                    {
                        continue;
                    }

                    var fact = pair.Value;
                    detail?.Count(DataFlowCounter.IndexAcceptedDefinitions);
                    Add(_byLocation, fact.LocationKey, definitionOrdinal,
                        DataFlowCounter.LocationBuildPostings);
                    Add(_byRoot, FactRootKey(fact), definitionOrdinal,
                        DataFlowCounter.RootBuildPostings);
                    Add(_byBase, fact.BaseKey, definitionOrdinal, DataFlowCounter.BaseBuildPostings);
                }
                detail?.Count(DataFlowCounter.LocationKeys, _byLocation.Count);
                detail?.Count(DataFlowCounter.RootKeys, _byRoot.Count);
                detail?.Count(DataFlowCounter.BaseKeys, _byBase.Count);
            }

            internal bool TryGetCandidates(DefinitionFact usedFact, SparseSetStore reachingDefinitions, int reachingSlot, out IReadOnlyList<NLCPGNode> candidates)
            {
                long start = _detail is null ? 0 : Stopwatch.GetTimestamp();
                _detail?.Count(DataFlowCounter.TryGetCandidatesCalls);
                if (string.IsNullOrEmpty(usedFact.LocationKey))
                {
                    _detail?.Count(DataFlowCounter.EmptyLocationReturns);
                    candidates = Array.Empty<NLCPGNode>();
                    _detail?.Elapsed(DataFlowDetail.TryGetCandidates, start);
                    return false;
                }

                _matches.Clear();
                AddReachable(_byLocation, usedFact.LocationKey, reachingDefinitions, reachingSlot,
                    DataFlowCounter.LocationQueries);
                AddReachable(_byRoot, usedFact.BaseKey, reachingDefinitions, reachingSlot,
                    DataFlowCounter.RootQueries);
                // Base 只在 BaseKey 为 null 时查询。
                //
                // 证明：非空查询基址 b 时 FactRootKey(usedFact) == usedFact.BaseKey == b，
                // 而 _byBase[b] ⊆ _byRoot[b]（_byBase 以 fact.BaseKey 建桶，_byRoot 以
                // FactRootKey(fact) = fact.BaseKey ?? fact.LocationKey 建桶，故 BaseKey == b 的
                // 定义在两者中落在同一个键上，_byRoot[b] 只是额外多出 BaseKey 为 null 的定义）。
                // Root 先扫、reachingSlot 相同、中间不清 _seen，因此后扫 Base 不可能再追加候选。
                //
                // BaseKey 为 null 时 Root 不执行（AddReachable 对空键早返回），而
                // _byBase[LocationKey] 正是"整体使用匹配到组成部分"的 container 分支
                // （IsContainerMatch 要求 reachingFact.BaseKey == usedFact.LocationKey），
                // 必须保留，故此处回退到 LocationKey 查询。
                if (usedFact.BaseKey is null)
                {
                    AddReachable(_byBase, usedFact.LocationKey, reachingDefinitions, reachingSlot,
                        DataFlowCounter.BaseQueries);
                }
                _detail?.Maximum(DataFlowCounter.MatchesCountHighWater, _matches.Count);
                _detail?.Maximum(DataFlowCounter.MatchesCapacityHighWater, _matches.Capacity);
                _detail?.Maximum(DataFlowCounter.TouchedCountHighWater, _touched.Count);
                _detail?.Maximum(DataFlowCounter.TouchedCapacityHighWater, _touched.Capacity);

                // 回滚本轮的置位标记，使 _seen 的语义与“每次调用新建 HashSet”完全一致。
                for (var i = 0; i < _touched.Count; i += 1)
                {
                    _seen[_touched[i]] = false;
                    _detail?.Count(DataFlowCounter.TouchedClears);
                }

                _touched.Clear();
                candidates = _matches;
                _detail?.Count(DataFlowCounter.IndexedReturns);
                _detail?.Count(DataFlowCounter.ReturnedCandidates, _matches.Count);
                _detail?.Elapsed(DataFlowDetail.TryGetCandidates, start);
                return true;
            }

            private void Add(Dictionary<string, List<int>> index, string? key, int definitionOrdinal,
                DataFlowCounter postingCounter)
            {
                if (string.IsNullOrEmpty(key))
                {
                    return;
                }

                if (!index.TryGetValue(key, out var ordinals))
                {
                    ordinals = new List<int>();
                    index[key] = ordinals;
                }

                ordinals.Add(definitionOrdinal);
                _detail?.Count(postingCounter);
            }

            private void AddReachable(Dictionary<string, List<int>> index, string? key,
                SparseSetStore reachingDefinitions, int reachingSlot, DataFlowCounter queryCounter)
            {
                if (string.IsNullOrEmpty(key))
                {
                    return;
                }
                _detail?.Count(queryCounter);
                if (!index.TryGetValue(key, out var ordinals))
                {
                    return;
                }
                _detail?.Count(queryCounter + 1); // Hit.

                foreach (var definitionOrdinal in ordinals)
                {
                    _detail?.Count(queryCounter + 2); // Posting visited.
                    _detail?.Count(queryCounter + 3); // Actual Contains call.
                    if (!reachingDefinitions.Contains(reachingSlot, definitionOrdinal))
                    {
                        _detail?.Count(queryCounter + 4); // Unreachable.
                        continue;
                    }
                    _detail?.Count(queryCounter + 5); // Actual seen check after Contains succeeds.
                    if (_seen[definitionOrdinal])
                    {
                        _detail?.Count(queryCounter + 6);
                        _detail?.Count(DataFlowCounter.SeenRejects);
                        continue;
                    }
                    _seen[definitionOrdinal] = true;
                    _touched.Add(definitionOrdinal);
                    _matches.Add(_definitionNodes[definitionOrdinal]);
                    _detail?.Count(queryCounter + 7);
                    _detail?.Count(DataFlowCounter.TouchedMarks);
                }
            }
        }

        private static NLCPGNode ResolveCandidateNode(NLCPGGraph graph, StableNodeAnchor anchor)
        {
            return graph.AddNode(new NLCPGNode(
              anchor.Kind,
              StableAnchor: anchor));
        }

        private sealed record CfgSensitivePartition(
          int Order,
          LocalFlowCandidateSet Candidates,
          NLCPGDataFlowOverflowReason OverflowReason,
          NLCPGDataFlowMethodMetrics Metrics);

        private sealed record CfgSensitiveWorkResult(
            MethodDataFlowPlan Plan,
            CfgSensitivePartition Partition,
            DataFlowMethodProbe? Probe,
            CpgWorkItem Item,
            DataFlowDiagnosticRequest? DiagnosticRequest);

        private sealed record DataFlowWorkBatchResult(
            IReadOnlyList<CfgSensitiveWorkResult> Partitions);

        /// <summary>
        /// 本阶段捕获的文档集合（D1 按项路由所需的「该文件的 context」来源）。
        /// </summary>
        /// <remarks>
        /// 由提交步在**开始消费 plan 之前**赋值。规划相位（<c>PlanDataFlowStage</c>）
        /// 同样需要它来逐文件构造工作项，故两处都设为同一集合。
        /// </remarks>
        private IReadOnlyList<NLCPGBuildContext> _currentDocuments = Array.Empty<NLCPGBuildContext>();

        /// <summary>
        /// 本阶段的路由基准 context（提供 <c>ResolveGraph</c>）。
        /// </summary>
        /// <remarks>
        /// 由提交步赋值。它只在**单文件**时被 <see cref="ResolveGraph"/> 真正使用
        /// （无文档集时恒返回该 context 的图）；多文件时 <c>ResolveGraph</c> 内部走注册表。
        /// </remarks>
        private NLCPGBuildContext? _currentBuildContextForRouting;

        internal void RunDataFlowPass(NLCPGBuildContext context)
        {
            // G0-P R-3：本阶段的规划已在【执行相位之前】完成并登记（见 PlanStagesBeforeExecution）。
            // 这里只消费既有 plan。
            var plan = TakeRecordedStagePlan(StageDependencyTable.Stage.DataFlow);
            if (plan is null)
            {
                // PlanDataFlowStage 恒返回非 null，故走到这里说明**规划相位没有覆盖本阶段**
                // （例如绕过了 RunOptionalPass 的其它入口）。fail-closed，而不是静默跳过
                // 整阶段的数据流边——正是 G0-P 要消灭的失效形态。
                throw new InvalidOperationException(
                  "DataFlow 阶段被请求执行，但规划相位没有登记它的 plan（G0-P R-3）。"
                  + "规划必须在任何 worker 启动之前完成；此处不重新规划，以免把规划时点拉回执行相位。");
            }

            CommitDataFlowStage(context, plan);
        }

        /// <summary>
        /// G0-P **R-3** 规划步：**只读**——算出批次，不触碰图。
        /// <para>
        /// <b>为何可静态规划（源码确证，逐条）：</b>
        /// <list type="bullet">
        /// <item><c>CreateDataFlowMethodPartitions</c> —— 唯一输入是
        /// <c>context.OperationInventory</c>，该集合由 <b>Operation 阶段</b>填充，
        /// 而 Operation 阶段位于本规划相位**之前**；</item>
        /// <item><c>AssembleDataFlowWorkBatches</c> —— 只读各分区语法树的**行号**
        /// 并调用无状态的 <c>CpgWorkBatchBuilder.Build</c>。</item>
        /// </list>
        /// </para>
        /// <para>
        /// ⚠ <b>刻意不在本步做的事：读 CFG 邻接。</b>本阶段真正的运行期依赖是
        /// <c>_cfgPredecessors/SuccessorsByNode</c>（由 <c>ControlFlow</c> 阶段写入，
        /// 依赖④），它只在 <c>BuildCfgAdjacency</c> 被读——那属于**计算**相位。
        /// 规划相位早于 ControlFlow 执行，故 <b>plan 里不可能含邻接，这是设计事实</b>：
        /// 把邻接塞进 plan 就等于把运行期状态偷渡进规划相位。
        /// 规划只需"规模"（批次与行号估算），不需要"内容"，这正是 R-3 的立足点。
        /// </para>
        /// <para>
        /// <b>恒返回非 null</b>（即使无方法分区）：与 <c>ControlFlow</c> 的"无操作根即返回 null"
        /// **不同**，因为本阶段在无分区时**仍要执行摘要流**（<c>AddCallArgumentAndReturnDataFlow</c>）
        /// 并回报 <c>_dataFlowWorkerCount</c>；若在此返回 null，会改变这些既有可观测值。
        /// </para>
        /// </summary>
        private StagePlan PlanDataFlowStage(NLCPGBuildContext context)
        {
            _currentDocuments = context.Documents;
            var methodPartitionsByFile = CreateDataFlowMethodPartitionsByFile(context);
            return new StagePlan(
              StageDependencyTable.Stage.DataFlow,
              AssembleDataFlowWorkBatches(methodPartitionsByFile));
        }

        /// <summary>
        /// **按文件**建立方法分区表（D1）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠ 原实现返回**扁平**列表，且 <c>AnalyzeDataFlowWorkBatch</c> 用
        /// <c>methodPartitions[item.StableOrder]</c> 下标它。<c>StableOrder</c> 是
        /// **文件内局部**序号 ⇒ 跨文件批次下该下标必然错配（分析到别的方法）。
        /// </para>
        /// <para>
        /// 按文件分桶后，下标域重新回到「单文件局部」，与单文件路径**逐字同构**；
        /// 无需给 <c>StableOrder</c> 引入全局语义（那会破坏 6 处既有下标/键用法）。
        /// </para>
        /// <para>
        /// 单文件时返回仅含一个条目的字典，且该条目与改造前的扁平列表
        /// **逐元素相同**（同一构造过程、同一顺序）。
        /// </para>
        /// </remarks>
        private Dictionary<string, IReadOnlyList<DataFlowMethodPartition>> CreateDataFlowMethodPartitionsByFile(
          NLCPGBuildContext context)
        {
            var partitionsByFile = new Dictionary<string, IReadOnlyList<DataFlowMethodPartition>>(
              StringComparer.Ordinal);
            foreach (var document in context.Documents)
            {
                partitionsByFile[document.FilePath] =
                  CreateDataFlowMethodPartitions(document.OperationInventory);
            }

            return partitionsByFile;
        }

        /// <summary>
        /// G0-P **R-3** 提交步：消费 plan，执行 worker 并发布（**唯一写图者**）。
        /// <para>
        /// ⚠ 与 <c>DominancePass.CommitDominanceStage</c> 同一形态：commit **重新求**方法分区与
        /// 操作索引（它们是对 <c>OperationInventory</c> 的纯派生，属"输入准备"），
        /// 但**只消费 <c>plan.Batches</c>**，绝不重新规划批次。
        /// 这一分工是有意的：若 commit 连批次都重算，plan 就成了摆设，
        /// 而"规划真的前移了"也就无法被任何用例区分（见附录 X 的变异②）。
        /// </para>
        /// </summary>
        private void CommitDataFlowStage(NLCPGBuildContext context, StagePlan plan)
        {
            // D1：操作索引按图无关的方式构造，但**图**必须按文件解析（见下）。
            _currentDocuments = context.Documents;
            _currentBuildContextForRouting = context;
            var methodPartitionsByFile = CreateDataFlowMethodPartitionsByFile(context);
            var operationIndexesByFile = new Dictionary<string, DataFlowOperationIndex>(StringComparer.Ordinal);
            foreach (var document in context.Documents)
            {
                _ = methodPartitionsByFile.TryGetValue(document.FilePath, out var filePartitions);
                operationIndexesByFile[document.FilePath] = CreateDataFlowOperationIndex(
                  document.OperationInventory,
                  (filePartitions ?? Array.Empty<DataFlowMethodPartition>())
                    .Select(partition => (IOperation)partition.MethodBlock).ToArray());
            }

            // ── fail-closed：plan 的条目必须能对应当前方法分区 ──────────────────────
            // AnalyzeDataFlowWorkBatch 以 `item.StableOrder` **索引**该文件的分区表，
            // 故若 plan 是在不同分区集合下算出的（例如规划时输入尚未就绪），
            // 提交步会**静默**分析错误的方法——产物看起来正常，内容却是错的。
            // 这里做 O(条目数) 的范围校验，不重算批次（重算会在热路径上翻倍工作量）。
            foreach (var batch in plan.Batches)
            {
                foreach (var item in batch.Items)
                {
                    if (!methodPartitionsByFile.TryGetValue(item.SourceFilePath, out var filePartitions))
                    {
                        throw new InvalidOperationException(
                          $"DataFlow 的 plan 引用了未登记的文件：{item.SourceFilePath}（G0-P R-3 / D1）。");
                    }

                    if ((uint)item.StableOrder >= (uint)filePartitions.Count)
                    {
                        throw new InvalidOperationException(
                          $"DataFlow 的 plan 与提交步输入不自洽：批次 item 的 StableOrder={item.StableOrder} "
                          + $"超出 {item.SourceFilePath} 当前方法分区数 {filePartitions.Count}（G0-P R-3 / D1）。"
                          + "plan 必须在与提交步**相同**的分区输入下算出，否则会静默分析错误的方法。");
                    }
                }
            }

            RunDataFlowPipeline(methodPartitionsByFile, operationIndexesByFile, plan);
            // 方法内数据流补完后，再单独处理跨调用/属性访问的摘要流。
            // ⚠ 摘要流是**跨方法**的（调用点与目标方法可能分处不同文件），
            //   故它按**每条事实自己的文件**路由，而不是按单一 context.Graph。
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

        private static UsedFactPartition AnalyzeUsedFactPartition(
            DataFlowMethodPartition methodPartition, int order, DataFlowMethodProbe? detail)
        {
            var orderedOperations = methodPartition.OrderedOperations;
            var usedFactsByOperation = new Dictionary<IOperation, UsedFactRecord>(
              ReferenceEqualityComparer.Instance);
            var retainedRecordCount = orderedOperations.Length;
            for (var operationIndex = orderedOperations.Length - 1; operationIndex >= 0; operationIndex -= 1)
            {
                var operation = orderedOperations[operationIndex];
                detail?.Count(DataFlowCounter.UsedOperationVisits);
                var directFacts = DirectUsedFacts(operation).ToArray();
                detail?.Count(DataFlowCounter.DirectUsedFacts, directFacts.Length);
                var childRecords = ObserveVisits(operation.ChildOperations, detail,
                    DataFlowCounter.UsedChildVisits)
                  .Where(usedFactsByOperation.ContainsKey)
                  .Select(child => usedFactsByOperation[child])
                  .ToArray();
                var factCount = directFacts.Length + ObserveVisits(childRecords, detail,
                    DataFlowCounter.UsedChildFactVisits).Sum(record => record.FactCount);
                usedFactsByOperation[operation] = new UsedFactRecord(directFacts, childRecords, factCount);
                retainedRecordCount += factCount;
            }

            return new UsedFactPartition(
              order,
              orderedOperations,
              usedFactsByOperation,
              retainedRecordCount);
        }

        private void RunDataFlowPipeline(
          IReadOnlyDictionary<string, IReadOnlyList<DataFlowMethodPartition>> methodPartitionsByFile,
          IReadOnlyDictionary<string, DataFlowOperationIndex> operationIndexesByFile,
          StagePlan plan)
        {
            // G0-P R-3：批次**只从 plan 取**，本方法不再持有"自行构造批次"的能力。
            // 把入参类型定为 StagePlan（而非裸 IReadOnlyList<CpgWorkBatch>）是刻意的：
            // 后者让提交步可以顺手再 Assemble 一次而**看不出差别**——
            // 那样 plan 就沦为摆设，而"规划真的前移了"将无法被任何行为用例区分
            // （附录 X.5 变异②：这种"重算"与"消费 plan"在产物上完全等价，行为测试抓不住）。
            // 收窄类型后，"重算"不再是顺手可写的形态。
            var workBatches = plan.Batches;
            _dataFlowBatchCount = workBatches.Count;
            _dataFlowWorkerCount = _workBatchExecutor.WorkerCount;
            long readyTimestamp = DataFlowDiagnostics is null ? 0 : Stopwatch.GetTimestamp();
            // 本方法只使用 reducer 的提交副作用，不读取返回列表：走消费入口，
            // 避免执行器在阶段运行期间累计保留已提交的 DataFlowWorkBatchResult。
            _workBatchExecutor.ExecuteAndConsumeAsync(
              workBatches,
              (batch, _, cancellationToken) => AnalyzeDataFlowWorkBatch(
                batch,
                methodPartitionsByFile,
                operationIndexesByFile,
                readyTimestamp,
                cancellationToken),
              // ⚠ D1：图按**每个分区自己所属文件**解析（一个批次可跨文件——T3）。
              result => CommitDataFlowWorkBatch(result),
              CancellationToken.None,
              CpgWorkBatchPerformanceStageId.DataFlow,
              result => new CpgWorkBatchResultMetrics(
                result.Partitions.Sum(partition => partition.Plan.FlowNodes.Length),
                result.Partitions.Sum(partition => partition.Partition.Candidates.EdgeCandidates.Count),
                result.Partitions.Sum(partition => partition.Plan.FlowNodes.Length * 64 + partition.Partition.Candidates.EdgeCandidates.Count * 48))).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 取某文件应写入的图（D1 路由）。
        /// </summary>
        /// <remarks>
        /// 转发到 <see cref="NLCPGBuildContext.ResolveGraph"/>；
        /// 无文档集（单文件）时恒返回构造期那张图，行为与改造前逐字一致。
        /// </remarks>
        private NLCPGGraph ResolveGraph(string sourceFilePath)
        {
            return _currentBuildContextForRouting!.ResolveGraph(sourceFilePath);
        }

        private IReadOnlyList<CpgWorkBatch> AssembleDataFlowWorkBatches(
          IReadOnlyDictionary<string, IReadOnlyList<DataFlowMethodPartition>> methodPartitionsByFile)
        {
            // G0-P R-3：计数仅供契约测试判定"规划**只算一次**"。
            // 为什么需要：若提交步无视 plan、自行重算批次，产物与"消费 plan"**完全等价**
            // （同一个纯函数、同一份输入）⇒ 任何行为断言都区分不了（附录 X.5 变异②）。
            // 而"每阶段只规划一次"本就是本仓库的既有不变量（RecordStagePlan 对重复登记抛异常），
            // 故用调用计数把它钉住是恰当的，且不引入新的行为差异。
            _dataFlowPlanAssemblyCount += 1;
            // D1：逐文件构造工作项——每个 item 的 StableOrder 是**该文件内**的分区下标，
            //   与 methodPartitionsByFile[该文件] 的下标域一致。
            //   单文件时产出与原实现逐字相同（同一 order 序列、同一 builder 调用）。
            var workItems = new List<CpgWorkItem>();
            foreach (var document in _currentDocuments)
            {
                if (!methodPartitionsByFile.TryGetValue(document.FilePath, out var methodPartitions))
                {
                    continue;
                }

                var order = 0;
                foreach (var partition in methodPartitions)
                {
                    var lineSpan = partition.MethodBlock.Syntax.SyntaxTree.GetLineSpan(partition.MethodBlock.Syntax.Span);
                    var estimate = CpgWorkBatchCostModel.Estimate(
                      lineSpan.StartLinePosition.Line,
                      lineSpan.EndLinePosition.Line,
                      _options.EffectiveWorkBatchCostOptions);
                    workItems.Add(new CpgWorkItem(
                      order,
                      document.FilePath,
                      partition.OwningMethod?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                      partition.MethodBlock.Syntax.SpanStart,
                      partition.MethodBlock.Syntax.Span.End,
                      estimate.Cost,
                      CpgWorkItemKind.Method));
                    order += 1;
                }
            }

            if (workItems.Count == 0)
            {
                return Array.Empty<CpgWorkBatch>();
            }

            return _workBatchExecutor is null
              ? Array.Empty<CpgWorkBatch>()
              // S5-2：走 BuildWorkBatches（装箱的**唯一入口**），由其套用全局分片序号。
              : BuildWorkBatches(workItems[0].SourceFilePath, workItems);
        }

        private DataFlowWorkBatchResult AnalyzeDataFlowWorkBatch(
          CpgWorkBatch batch,
          IReadOnlyDictionary<string, IReadOnlyList<DataFlowMethodPartition>> methodPartitionsByFile,
          IReadOnlyDictionary<string, DataFlowOperationIndex> operationIndexesByFile,
          long readyTimestamp,
          CancellationToken cancellationToken)
        {
            var results = new List<CfgSensitiveWorkResult>(batch.Items.Count);
            foreach (var item in batch.Items.OrderBy(item => item.StableOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // ⚠ **下标域（D1）**：用 item 自己文件的**该文件**分区表与操作索引，
                //   故 item.StableOrder（文件内局部序号）的下标域与之严格一致。
                //   旧实现用单一扁平表，跨文件批次下会分析到错误的方法（静默错配）。
                var methodPartitions = methodPartitionsByFile[item.SourceFilePath];
                var operationIndex = operationIndexesByFile[item.SourceFilePath];
                if ((uint)item.StableOrder >= (uint)methodPartitions.Count)
                {
                    throw new InvalidOperationException(
                      $"Data-flow WorkBatch item {item.StableOrder} has no method partition "
                      + $"in '{item.SourceFilePath}'.");
                }

                MethodDataFlowPlan? plan = null;
                var request = DataFlowDiagnostics;
                var probe = request is null ? null : new DataFlowMethodProbe(request.Mode);
                var detail = request?.Mode == DataFlowDiagnosticMode.Detailed ? probe : null;
                long methodStart = probe is null ? 0 : Stopwatch.GetTimestamp();
                if (probe is not null)
                {
                    probe.QueueWaitTicks = methodStart - readyTimestamp;
                }
                try
                {
                    var methodPartition = methodPartitions[item.StableOrder];
                    probe?.Begin(DataFlowPhase.UsedFacts);
                    var usedFacts = AnalyzeUsedFactPartition(
                        methodPartition, item.StableOrder, detail);
                    probe?.Begin(DataFlowPhase.PlanBuild);
                    plan = BuildCfgSensitivePartitionPlan(
                        usedFacts, methodPartition, operationIndex, detail, item.SourceFilePath);
                    var partition = AnalyzeCfgSensitivePartition(
                        plan, _options.EffectiveDataFlowOptions, probe, detail);
                    results.Add(new CfgSensitiveWorkResult(plan, partition, probe, item, request));
                    plan = null;
                }
                finally
                {
                    if (probe is not null)
                    {
                        long methodEnd = Stopwatch.GetTimestamp();
                        probe.End(methodEnd);
                        probe.MethodTotalTicks = methodEnd - methodStart;
                    }
                    plan?.Release();
                }
            }

            return new DataFlowWorkBatchResult(results);
        }

        /// <summary>
        /// 归并步：把每个分区写入**它自己所属文件**的图（D1 按项路由）。
        /// </summary>
        /// <remarks>
        /// 一个批次可跨多个文件（T3 已放宽装箱）。旧实现收单一 <c>graph</c>，
        /// 会把 B 文件的数据流边写进 A 文件的图。
        /// 分区按 <c>Item.SourceFilePath</c> 逐条解析目标图，故无需假设批内同文件。
        /// </remarks>
        private void CommitDataFlowWorkBatch(DataFlowWorkBatchResult result)
        {
            foreach (var partition in result.Partitions.OrderBy(partition => partition.Plan.Order))
            {
                try
                {
                    // G0-P R.3 L1：本方法是**归并回调**（执行器已为归并线程开共享态窗口）。
                    // 这两处 builder 级 List 记账写入走 WriteSharedState。
                    //
                    // ⚠ 如实记录：按**当前**代码并非活跃竞争——本阶段归并回调相互串行
                    //   （SingleReader），且 worker 不写这两个列表。本机制消除的是
                    //   "不变式靠巧合成立"：List<T>.Add 非线程安全，一旦将来出现
                    //   并发追加，失效形态是丢条目/写坏内部数组，**不抛异常**。
                    WriteSharedState(
                      () => _dataFlowMethodMetrics.Add(
                        partition.Partition.Metrics with
                        {
                          SourceFilePath = partition.Item.SourceFilePath
                        }));

                    long commitStart = partition.Probe is null ? 0 : Stopwatch.GetTimestamp();
                    var graph = ResolveGraph(partition.Item.SourceFilePath);
                    CommitCfgSensitivePartition(partition.Plan, partition.Partition, graph);
                    if (partition.Probe is { } probe && partition.DiagnosticRequest is { } request)
                    {
                        probe.CommitTicks = Stopwatch.GetTimestamp() - commitStart;
                        var item = partition.Item;
                        WriteSharedState(
                          () => _dataFlowDiagnostics.Add(new DataFlowDiagnosticResult(
                              request.RunId, request.InputHash, item.SourceFilePath,
                              partition.Plan.MethodFullName, item.SpanStart, item.SpanEnd,
                              item.StableOrder, request.Mode, Stopwatch.Frequency,
                              probe.MethodTotalTicks, probe.QueueWaitTicks, probe.CommitTicks,
                              probe.PhaseSnapshot(), probe.DetailSnapshot(), probe.CounterSnapshot(),
                              Array.AsReadOnly(partition.Partition.Candidates.EdgeCandidates.ToArray()),
                              partition.Partition.Metrics,
                              partition.Partition.OverflowReason == NLCPGDataFlowOverflowReason.None
                                  ? "Complete" : partition.Partition.OverflowReason.ToString())));
                    }
                }
                finally
                {
                    partition.Plan.Release();
                }
            }
        }

        private MethodDataFlowPlan BuildCfgSensitivePartitionPlan(
            UsedFactPartition partition, DataFlowMethodPartition methodPartition,
            DataFlowOperationIndex operationIndex, DataFlowMethodProbe? detail,
            string graphFilePath)
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
                    detail?.Count(DataFlowCounter.PlanOperationVisits);
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
                        detail?.Count(DataFlowCounter.PlanParameterVisits);
                        var parameterNode = RequireMethodParameterNode(graphFilePath, methodSymbol, parameter);
                        AddUniqueFlowNode(flowNodes, flowNodeSet, parameterNode);
                        parameterDefinitionFacts[parameterNode] = DefinitionFactForParameter(parameter);
                    }
                }

                foreach (var operationNode in operationNodes)
                {
                    detail?.Count(DataFlowCounter.PlanOperationNodeVisits);
                    AddUniqueFlowNode(flowNodes, flowNodeSet, operationNode);
                }

                NLCPGNode? returnNode = null;
                NLCPGNode? exitNode = null;
                var methodFullName = $"method-partition-{partition.Order}";
                if (methodPartition.OwningMethod is IMethodSymbol flowMethodSymbol)
                {
                    methodFullName = flowMethodSymbol.ToDisplayString();
                    // return/exit 只在方法实际需要返回值或退出流时加入计划。
                    returnNode = RequireMethodReturnNode(graphFilePath, flowMethodSymbol);
                    AddUniqueFlowNode(flowNodes, flowNodeSet, returnNode.Value);
                    if (ObserveVisits(orderedOperations, detail, DataFlowCounter.PlanReturnScanVisits)
                        .OfType<IReturnOperation>().Any(operation => operation.ReturnedValue is not null))
                    {
                        exitNode = RequireMethodExitNode(graphFilePath, flowMethodSymbol);
                        AddUniqueFlowNode(flowNodes, flowNodeSet, exitNode.Value);
                    }
                }

                var flowNodeArray = flowNodes.ToArray();
                // 节点 → 序号映射在计划构建期就建好（原先在分析期重建），
                // 供 CSR 构造与后续 fixpoint / 候选循环共用，避免重复建表。
                var flowNodeOrdinals = ObserveVisits(flowNodeArray, detail, DataFlowCounter.FlowOrdinalVisits)
                  .Select((node, ordinal) => (node, ordinal))
                  .ToDictionary(entry => entry.node, entry => entry.ordinal);
                BuildFlowNeighborCsr(
                    flowNodeArray, flowNodeOrdinals, incoming: true, detail,
                    out var predecessorOffsets, out var predecessorOrdinals);
                BuildFlowNeighborCsr(
                    flowNodeArray, flowNodeOrdinals, incoming: false, detail,
                    out var successorOffsets, out var successorOrdinals);
                return new MethodDataFlowPlan(
                  partition.Order,
                  methodFullName,
                  orderedOperations,
                  flowNodeArray,
                  operationNodes,
                  partition.UsedFactsByOperation,
                  parameterDefinitionFacts,
                  operationNodesByOperation,
                  returnNode,
                  exitNode,
                  flowNodeOrdinals,
                  predecessorOffsets,
                  predecessorOrdinals,
                  successorOffsets,
                  successorOrdinals);
        }

        private NLCPGNode RequireMethodParameterNode(string graphFilePath, IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol)
        {
            var key = $"methodparam:{SymbolId(CanonicalMethodSymbol(methodSymbol))}:{parameterSymbol.Ordinal}";
            return _methodParameterNodes.TryGetValue(ResolveGraph(graphFilePath), key, out var node)
              ? node
              : throw new InvalidOperationException(
                $"Method parameter node was not published before data-flow collection: {key}.");
        }

        private NLCPGNode RequireMethodReturnNode(string graphFilePath, IMethodSymbol methodSymbol)
        {
            var key = $"methodreturn:{SymbolId(CanonicalMethodSymbol(methodSymbol))}";
            return _methodReturnNodes.TryGetValue(ResolveGraph(graphFilePath), key, out var node)
              ? node
              : throw new InvalidOperationException(
                $"Method return node was not published before data-flow collection: {key}.");
        }

        private NLCPGNode RequireMethodExitNode(string graphFilePath, IMethodSymbol methodSymbol)
        {
            var key = $"methodexit:{SymbolId(CanonicalMethodSymbol(methodSymbol))}";
            return _methodExitNodes.TryGetValue(ResolveGraph(graphFilePath), key, out var node)
              ? node
              : throw new InvalidOperationException(
                $"Method exit node was not published before data-flow collection: {key}.");
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

        private static CfgSensitivePartition AnalyzeCfgSensitivePartition(
            MethodDataFlowPlan plan, NLCPGDataFlowOptions options,
            DataFlowMethodProbe? probe, DataFlowMethodProbe? detail)
        {
            probe?.Begin(DataFlowPhase.DefinitionSetup);
            var definitionFactsByNode = new Dictionary<NLCPGNode, DefinitionFact>(
              plan.ParameterDefinitionFacts);

            // 第一阶段：把“哪些节点定义了什么”整理成节点索引。
            foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
            {
                var definedFact = DefinedFact(operationNodePair.First);
                detail?.Count(DataFlowCounter.DefinitionOperationVisits);
                if (definedFact is { } fact)
                {
                    definitionFactsByNode[operationNodePair.Second] = fact;
                }
            }
            // 位集只会被定义节点置位（唯一的置位语句位于定义节点分支内），
            // 因此按定义数分配位宽，并用紧凑定义序数寻址每个 bit。
            var flowNodes = plan.FlowNodes;
            var definitionOrdinals = new int[flowNodes.Length];
            var definitionNodes = new List<NLCPGNode>(definitionFactsByNode.Count);
            var definitionFactsByDefinitionOrdinal = new List<DefinitionFact>(definitionFactsByNode.Count);
            for (var ordinal = 0; ordinal < flowNodes.Length; ordinal += 1)
            {
                detail?.Count(DataFlowCounter.DefinitionFlowVisits);
                if (definitionFactsByNode.TryGetValue(flowNodes[ordinal], out var definitionFact))
                {
                    definitionOrdinals[ordinal] = definitionNodes.Count;
                    definitionNodes.Add(flowNodes[ordinal]);
                    definitionFactsByDefinitionOrdinal.Add(definitionFact);
                }
                else
                {
                    definitionOrdinals[ordinal] = -1;
                }
            }

            var wordsPerSet = BitSetWordCount(definitionNodes.Count);
            detail?.Count(DataFlowCounter.DefinitionCount, definitionFactsByNode.Count);
            detail?.Count(DataFlowCounter.FlowNodeCount, flowNodes.Length);
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

            // 第二阶段：用方法局部整数编号和 bitset 初始化 out 集，避免节点 HashSet 的迭代分配。
            // in 集不在 fixpoint 内物化：它在循环内只写不读，收敛后可由前驱 out 集无损重算。
            // 序号映射由计划持有并在此复用，不再重建第二份。
            var flowNodeOrdinals = plan.FlowNodeOrdinals;
            // 位集尺寸用 long 计算：N × ceil(D/64) 在极端方法上会超出 int。
            // 这种情况按“流节点预算超限”交给既有策略处理，而不是抛 OverflowException 打断整次构建。
            // 注意：稀疏表示的实际存储是 N × (4 + 4k) 字节，与 D 无关，
            // 但此处仍按**位宇宙**的逻辑尺寸判定，以保持既有预算语义与溢出原因不变。
            var bitSetLength = (long)flowNodes.Length * wordsPerSet;
            if (bitSetLength > int.MaxValue)
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

            // 稀疏 out 集：N 个流节点槽位 + 2 个临时槽位（in 集 / updated）。
            var sets = new SparseSetStore(flowNodes.Length);
            detail?.Count(DataFlowCounter.InitializedSetSlots, flowNodes.Length + 2);
            var incomingSlot = sets.IncomingScratchSlot;
            var updatedSlot = sets.UpdatedScratchSlot;
            var worklist = new Queue<int>(flowNodes.Length);
            var queued = new bool[flowNodes.Length];
            for (var ordinal = 0; ordinal < flowNodes.Length; ordinal += 1)
            {
                worklist.Enqueue(ordinal);
                detail?.Count(DataFlowCounter.Enqueues);
                detail?.Count(DataFlowCounter.InitializedQueueSlots);
                queued[ordinal] = true;
            }
            // 第三阶段：标准 worklist fixpoint，按 predecessor/out 集传播 reaching definitions。
            var worklistIterations = 0;
            probe?.Begin(DataFlowPhase.Fixpoint);
            while (worklist.Count > 0)
            {
                var nodeOrdinal = worklist.Dequeue();
                detail?.Count(DataFlowCounter.Dequeues);
                queued[nodeOrdinal] = false;
                worklistIterations += 1;
                sets.Clear(incomingSlot);
                // 邻接已是序号：区间直接给出前驱序号，无需「节点 → 序号」查找。
                // 区间长度等于旧 Predecessors 数组长度（两者都只含本方法内的邻居），
                // 故 FixpointPredecessorVisits / FixpointUnions 的计数与旧实现逐次相同。
                var predecessorStart = plan.PredecessorOffsets[nodeOrdinal];
                var predecessorEnd = plan.PredecessorOffsets[nodeOrdinal + 1];
                for (var predecessorIndex = predecessorStart; predecessorIndex < predecessorEnd; predecessorIndex += 1)
                {
                    detail?.Count(DataFlowCounter.FixpointPredecessorVisits);
                    sets.Union(incomingSlot, plan.PredecessorOrdinals[predecessorIndex]);
                    detail?.Count(DataFlowCounter.FixpointUnions);
                }

                sets.Copy(updatedSlot, incomingSlot);
                var definitionOrdinal = definitionOrdinals[nodeOrdinal];
                if (definitionOrdinal >= 0)
                {
                    detail?.Count(DataFlowCounter.Transfers);
                    sets.ApplyDefinitionTransfer(
                      updatedSlot,
                      definitionOrdinal,
                      definitionFactsByDefinitionOrdinal[definitionOrdinal],
                      definitionFactsByDefinitionOrdinal);
                }

                detail?.Count(DataFlowCounter.SetComparisons);
                if (sets.SetEquals(nodeOrdinal, updatedSlot))
                {
                    continue;
                }

                sets.Copy(nodeOrdinal, updatedSlot);
                var successorStart = plan.SuccessorOffsets[nodeOrdinal];
                var successorEnd = plan.SuccessorOffsets[nodeOrdinal + 1];
                for (var successorIndex = successorStart; successorIndex < successorEnd; successorIndex += 1)
                {
                    detail?.Count(DataFlowCounter.FixpointSuccessorVisits);
                    var successorOrdinal = plan.SuccessorOrdinals[successorIndex];
                    if (!queued[successorOrdinal])
                    {
                        queued[successorOrdinal] = true;
                        worklist.Enqueue(successorOrdinal);
                        detail?.Count(DataFlowCounter.Enqueues);
                    }
                }
            }
            // 第四阶段：把 reaching-definition 命中转换成 DataFlow 候选边。
            probe?.Begin(DataFlowPhase.CandidateIndexBuild);
            var candidates = new DataFlowCandidateCollector(detail);
            var definitionFactIndex = new DefinitionFactIndex(
              definitionFactsByNode,
              flowNodeOrdinals,
              definitionOrdinals,
              definitionNodes, detail);
            probe?.Begin(DataFlowPhase.CandidateLoop);
            foreach (var operationNodePair in plan.OrderedOperations.Zip(plan.OperationNodes))
            {
                // in 集 = 该操作节点全部前驱 out 集的并（收敛后重算，等价于不物化 inSets）。
                detail?.Count(DataFlowCounter.CandidateOperationVisits);
                // 整棵子树都没有使用事实时，候选阶段的三步（清空 in 集、前驱 union、
                // TryGetCandidates）都不可能产生候选：TryGetCandidates 只被
                // EnumerateFacts() 驱动，而该记录 FactCount == 0 意味着任何 direct 或
                // 子记录事实都不存在（FactCount = DirectFacts.Length + 子记录 FactCount 之和）。
                // 落点必须在 sets.Clear 之前：这样既不付出 union 代价，也不改变后面对
                // 其它操作节点留下的缓存槽内容——incomingSlot 只在本次迭代内被读取。
                // fixpoint、definition setup、explicit sources 与 return boundary 都不受影响。
                // 记录只查一次，供下面的 guard 和 EnumerateFacts 共用。
                var usedFactRecord = plan.UsedFactsByOperation[operationNodePair.First];
                if (usedFactRecord.FactCount == 0)
                {
                    detail?.Count(DataFlowCounter.CandidateZeroFactSkips);
                    continue;
                }

                sets.Clear(incomingSlot);
                // 候选阶段同样直接用序号区间；区间长度与旧 Predecessors 数组长度相同，
                // 故 CandidatePredecessorVisits / CandidateUnions 计数不变。
                var candidateNodeOrdinal = flowNodeOrdinals[operationNodePair.Second];
                var candidateStart = plan.PredecessorOffsets[candidateNodeOrdinal];
                var candidateEnd = plan.PredecessorOffsets[candidateNodeOrdinal + 1];
                for (var candidateIndex = candidateStart; candidateIndex < candidateEnd; candidateIndex += 1)
                {
                    detail?.Count(DataFlowCounter.CandidatePredecessorVisits);
                    sets.Union(incomingSlot, plan.PredecessorOrdinals[candidateIndex]);
                    detail?.Count(DataFlowCounter.CandidateUnions);
                }

                if (sets.IsEmpty(incomingSlot))
                {
                    continue;
                }

                foreach (var usedFact in usedFactRecord.EnumerateFacts())
                {
                    detail?.Count(DataFlowCounter.UsedFactVisits);
                    if (definitionFactIndex.TryGetCandidates(
                      usedFact,
                      sets,
                      incomingSlot,
                      out var indexedCandidates))
                    {
                        foreach (var reachingDefinitionNode in indexedCandidates)
                        {
                            if (TryGetMeasuredDefinition(definitionFactsByNode, reachingDefinitionNode,
                                detail, fallback: false, out var reachingFact) &&
                                MeasuredFactsMatch(reachingFact, usedFact, detail, fallback: false) &&
                                !candidates.TryAdd(reachingDefinitionNode, operationNodePair.Second,
                                    options.MaxCandidateEdgesPerMethod, fallback: false))
                            {
                                return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                            }
                        }
                    }
                    else
                    {
                        foreach (var reachingDefinitionOrdinal in sets.Enumerate(incomingSlot))
                        {
                            detail?.Count(DataFlowCounter.FallbackOrdinals);
                            var reachingDefinitionNode = definitionNodes[reachingDefinitionOrdinal];
                            if (TryGetMeasuredDefinition(definitionFactsByNode, reachingDefinitionNode,
                                detail, fallback: true, out var reachingFact) &&
                                MeasuredFactsMatch(reachingFact, usedFact, detail, fallback: true) &&
                                !candidates.TryAdd(reachingDefinitionNode, operationNodePair.Second,
                                    options.MaxCandidateEdgesPerMethod, fallback: true))
                            {
                                return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                            }
                        }
                    }
                }
            }
            // 第五阶段：补显式值源流，让赋值/调用/return 的值来源可以直接连通。
            probe?.Begin(DataFlowPhase.ExplicitSources);
            foreach (var operation in plan.OrderedOperations)
            {
                detail?.Count(DataFlowCounter.ExplicitOperationVisits);
                foreach (var sourceOperation in ValueSourceOperations(operation))
                {
                    detail?.Count(DataFlowCounter.ExplicitSourceVisits);
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
            probe?.Begin(DataFlowPhase.ReturnBoundary);
            if (plan.ReturnNode is not null && plan.ExitNode is not null)
            {
                var returnNode = plan.ReturnNode.Value;
                var exitNode = plan.ExitNode.Value;
                foreach (var returnOperation in ObserveVisits(plan.OrderedOperations, detail,
                    DataFlowCounter.ReturnScanVisits).OfType<IReturnOperation>()
                    .Where(operation => operation.ReturnedValue is not null))
                {
                    detail?.Count(DataFlowCounter.ReturnValueVisits);
                    if (plan.OperationNodesByOperation.TryGetValue(returnOperation.ReturnedValue!, out var valueNode) &&
                        plan.OperationNodesByOperation.TryGetValue(returnOperation, out var returnOperationNode))
                    {
                        if (!candidates.TryAdd(valueNode, returnNode, options.MaxCandidateEdgesPerMethod) ||
                            !candidates.TryAdd(returnNode, exitNode, options.MaxCandidateEdgesPerMethod) ||
                            !candidates.TryAdd(returnOperationNode, exitNode, options.MaxCandidateEdgesPerMethod))
                        {
                            return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                        }
                    }
                }
            }
            // 第七阶段：没有显式 return 时，为末尾可顺序流出的语句补隐式返回值路径。
            if (plan.ReturnNode is not null && plan.OrderedOperations.FirstOrDefault() is IBlockOperation methodBlock)
            {
                var returnNode = plan.ReturnNode.Value;
                var terminalOperation = methodBlock.Operations.LastOrDefault();
                detail?.Count(DataFlowCounter.TerminalVisits, terminalOperation is null ? 0 : 1);
                if (terminalOperation is not null && !ContainsMeasuredExplicitReturn(methodBlock, detail) && !StopsSequentialFlow(terminalOperation) &&
                    plan.OperationNodesByOperation.TryGetValue(terminalOperation, out var terminalNode))
                {
                    if (!candidates.TryAdd(terminalNode, returnNode, options.MaxCandidateEdgesPerMethod))
                    {
                        return CreateCandidateLimitExceededPartition(plan, options, candidates, definitionFactsByNode.Count, wordsPerSet, worklistIterations);
                    }
                }
            }

            // 正常路径返回完整候选集；真正写图留给 ordered commit 阶段处理。
            probe?.Begin(DataFlowPhase.ResultMaterialization);
            detail?.Count(DataFlowCounter.MaterializedCandidates, candidates.UniqueCandidateCount);
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
                NLCPGDataFlowOverflowReason.None,
                sets.SpilledNodeCount));
        }

        private static NLCPGDataFlowMethodMetrics CreateDataFlowMetrics(
          MethodDataFlowPlan plan,
          int wordsPerSet,
          int definitionCount,
          int worklistIterations,
          DataFlowCandidateCollector? candidates,
          NLCPGDataFlowOverflowReason overflowReason,
          int sparseOverflowNodeCount = 0)
        {
            return new NLCPGDataFlowMethodMetrics(
              plan.MethodFullName,
              plan.FlowNodes.Length,
              wordsPerSet,
              definitionCount,
              worklistIterations,
              candidates?.RawCandidateCount ?? 0,
              candidates?.UniqueCandidateCount ?? 0,
              overflowReason,
              sparseOverflowNodeCount);
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

        // 把预先缓存的 CFG 邻接裁成「当前方法实际关心的节点子图」，直接落成 CSR。
        //
        // 关键点：**单遍**完成。旧实现是「逐节点建可变列表 → 再整体复制成数组」，
        // 需要缓存两遍之间保持稳定；这里按 flow 节点序追加，故 offsets[i + 1] 恒等于
        // 追加后的总数，无需计数/填充两遍，也就不存在缓存稳定性前置条件。
        //
        // 保持顺序：按 flow 节点序、并在每个节点内沿用缓存**原枚举顺序**（缓存是 HashSet，
        // 故其枚举顺序即原顺序）。不排序、不额外去重、不依赖全局 HashSet 枚举顺序。
        // 邻居的身份用 flowNodeOrdinals 判定归属，与旧实现 flowNodes.Contains 的 O(1) 语义相同。
        private void BuildFlowNeighborCsr(
            NLCPGNode[] flowNodes,
            Dictionary<NLCPGNode, int> flowNodeOrdinals,
            bool incoming,
            DataFlowMethodProbe? detail,
            out int[] offsets,
            out int[] ordinals)
        {
            // N + 1 个边界；N == 0 时为 int[1] { 0 }，与空 neighbors 一起表示零节点。
            offsets = new int[flowNodes.Length + 1];
            var flat = new List<int>(flowNodes.Length);

            for (var nodeOrdinal = 0; nodeOrdinal < flowNodes.Length; nodeOrdinal += 1)
            {
                offsets[nodeOrdinal] = flat.Count;
                var node = flowNodes[nodeOrdinal];
                detail?.Count(incoming
                    ? DataFlowCounter.PlanIncomingNodeInitializations
                    : DataFlowCounter.PlanOutgoingNodeInitializations);
                detail?.Count(incoming ? DataFlowCounter.PlanIncomingNodeVisits
                    : DataFlowCounter.PlanOutgoingNodeVisits);
                var cachedNeighbors = incoming
                  ? GetCachedCfgPredecessors(node)
                  : GetCachedCfgSuccessors(node);
                foreach (var neighborNode in cachedNeighbors)
                {
                    detail?.Count(incoming ? DataFlowCounter.PlanIncomingEdgeVisits
                        : DataFlowCounter.PlanOutgoingEdgeVisits);
                    if (flowNodeOrdinals.TryGetValue(neighborNode, out var neighborOrdinal))
                    {
                        detail?.Count(incoming ? DataFlowCounter.PlanIncomingEdgesRetained
                            : DataFlowCounter.PlanOutgoingEdgesRetained);
                        flat.Add(neighborOrdinal);
                    }
                }
            }

            // 末位边界必须落在 N 上（而非留在 0），否则最后一个节点的区间会被读成空。
            offsets[flowNodes.Length] = flat.Count;
            ordinals = flat.Count == 0 ? Array.Empty<int>() : flat.ToArray();
        }

        private static void AddUniqueFlowNode(List<NLCPGNode> flowNodes, HashSet<NLCPGNode> flowNodeSet, NLCPGNode node)
        {
            if (flowNodeSet.Add(node))
            {
                flowNodes.Add(node);
            }
        }

        private static int BitSetWordCount(int bitCount)
        {
            return Math.Max(1, (bitCount + 63) / 64);
        }

        /// <summary>
        /// 内联稀疏集合存储（设计 §3.1）：SoA 布局的"计数 + <c>k</c> 个内联槽位"，
        /// 第 <c>k+1</c> 个及以后的元素**外溢**到 lazy 分配的字典，**绝不截断**。
        ///
        /// 槽位布局：<c>[0, nodeCount)</c> 为流节点 out 集，
        /// 末尾额外 2 个槽位供 in 集与临时集使用（对应原先的 incoming/updated scratch）。
        ///
        /// 不变式（设计 §3.3，正确性的全部来源）：
        /// <list type="number">
        /// <item>双射：与定宽位集一一对应，可互相还原。</item>
        /// <item>溢出外溢，绝不截断。</item>
        /// <item>元素**严格升序且无重复**。</item>
        /// <item>相等比较逐元素精确（近似会把 fixpoint 提前收敛）。</item>
        /// <item><c>count == 0</c> 合法且高频（实测 93.9% 的 out-set 为空）。</item>
        /// <item>溢出只影响表示，不影响语义（绝不改 <c>OverflowReason</c>）。</item>
        /// </list>
        ///
        /// 升序是**正确性要求**而非风格：候选预算在去重前计数，
        /// 顺序改变会在预算路径上导致整方法丢失 DataFlow 边（设计 §3.5）。
        /// </summary>
        /// <remarks>
        /// <c>internal</c>（而非 <c>private</c>）是为了让 ContractTests 经现有
        /// <c>InternalsVisibleTo</c> 友元直接做**精确分配与共享别名**断言；
        /// 它不在任何公开 API 上出现，也不改变生产调用路径。
        /// </remarks>
        internal sealed class SparseSetStore
        {
            /// <summary>内联槽位数；由设计 §3.2 的真实分配实测裁定为 1。</summary>
            internal const int InlineSlotCount = 1;

            /// <summary>用于避免每次操作都分配临时缓冲的内联容量。</summary>
            private const int StackBufferCapacity = 8;

            /// <summary>
            /// 槽位的**只读**视图：在捕获瞬间把唯一的那个内联槽读成局部标量，并持有 spill 引用，
            /// 不复制 spill 内容、不枚举、不分配。
            ///
            /// 内联部分必须**捕获成标量**而不是留待读取 <c>_ordinals</c>：归并期间的写入端
            /// 会把并集首元素写回目标槽的内联位置，若视图仍从 <c>_ordinals</c> 读取，
            /// 当目标元素在归并中排在源元素之后时就会读到已被覆盖的新值（读写别名）。
            /// 标量捕获使归并全程只读捕获前的旧输入。
            ///
            /// **本视图按 <c>InlineSlotCount == 1</c> 写成**：只存一个内联标量。
            /// 这正是设计 §3.2 的裁定；若该常量改变，视图与归并写入端必须一并复核，
            /// 不能沿用这里的读取模型。
            /// </summary>
            private readonly struct SetView
            {
                private readonly int _inline;
                private readonly int[]? _spill;

                internal SetView(int inlineValue, int count, int[]? spill)
                {
                    _inline = inlineValue;
                    Count = count;
                    _spill = spill;
                }

                /// <summary>元素个数（捕获时的快照值）。</summary>
                internal int Count { get; }

                /// <summary>
                /// 按升序读取第 <paramref name="index"/> 个元素：下标 0 取捕获的内联标量，
                /// 其余取 spill 的 <c>index - 1</c>。无接口枚举、无闭包分配。
                /// </summary>
                internal int this[int index]
                {
                    get
                    {
                        if (index == 0)
                        {
                            return _inline;
                        }

                        return _spill![index - 1];
                    }
                }
            }

            private readonly int[] _counts;
            private readonly int[] _ordinals;
            private readonly int _nodeCount;

            /// <summary>仅在实际发生外溢时分配；溢出数组视为**不可变**，可安全共享引用。</summary>
            private Dictionary<int, int[]>? _overflow;

            internal SparseSetStore(int nodeCount)
            {
                _nodeCount = nodeCount;
                var slotCount = nodeCount + 2;
                _counts = new int[slotCount];
                _ordinals = new int[slotCount * InlineSlotCount];
            }

            /// <summary>in 集临时槽位。</summary>
            internal int IncomingScratchSlot => _nodeCount;

            /// <summary>updated 临时槽位。</summary>
            internal int UpdatedScratchSlot => _nodeCount + 1;

            /// <summary>发生外溢的**流节点**数（不含临时槽位），仅供诊断与测试。</summary>
            internal int SpilledNodeCount
            {
                get
                {
                    if (_overflow is null)
                    {
                        return 0;
                    }

                    var count = 0;
                    foreach (var slot in _overflow.Keys)
                    {
                        if (slot < _nodeCount)
                        {
                            count += 1;
                        }
                    }

                    return count;
                }
            }

            /// <summary>元素个数；<c>0</c> 是合法状态。</summary>
            internal int CountOf(int slot)
            {
                return _counts[slot];
            }

            internal bool IsEmpty(int slot)
            {
                return _counts[slot] == 0;
            }

            /// <summary>清空槽位（对应 <c>Array.Clear</c>）。</summary>
            internal void Clear(int slot)
            {
                _counts[slot] = 0;
                _overflow?.Remove(slot);
            }

            /// <summary>成员测试（对应 <c>IsBitSet</c>）：内联部分线性扫描，溢出部分二分。</summary>
            internal bool Contains(int slot, int ordinal)
            {
                var count = _counts[slot];
                if (count == 0)
                {
                    return false;
                }

                var baseIndex = slot * InlineSlotCount;
                var inline = Math.Min(count, InlineSlotCount);
                for (var i = 0; i < inline; i += 1)
                {
                    if (_ordinals[baseIndex + i] == ordinal)
                    {
                        return true;
                    }
                }

                if (count <= InlineSlotCount ||
                    _overflow is null ||
                    !_overflow.TryGetValue(slot, out var spill))
                {
                    return false;
                }

                var low = 0;
                var high = spill.Length - 1;
                while (low <= high)
                {
                    var middle = low + ((high - low) / 2);
                    var value = spill[middle];
                    if (value == ordinal)
                    {
                        return true;
                    }

                    if (value < ordinal)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle - 1;
                    }
                }

                return false;
            }

            /// <summary>按升序枚举元素（对应 <c>EnumerateSetBits</c>）；顺序与稠密版一致。</summary>
            internal IEnumerable<int> Enumerate(int slot)
            {
                var count = _counts[slot];
                var baseIndex = slot * InlineSlotCount;
                var inline = Math.Min(count, InlineSlotCount);
                for (var i = 0; i < inline; i += 1)
                {
                    yield return _ordinals[baseIndex + i];
                }

                if (count > InlineSlotCount &&
                    _overflow is not null &&
                    _overflow.TryGetValue(slot, out var spill))
                {
                    for (var i = 0; i < spill.Length; i += 1)
                    {
                        yield return spill[i];
                    }
                }
            }

            /// <summary>覆盖复制（对应 <c>CopyBitSet</c>）。</summary>
            internal void Copy(int targetSlot, int sourceSlot)
            {
                if (targetSlot == sourceSlot)
                {
                    return;
                }

                var count = _counts[sourceSlot];
                _counts[targetSlot] = count;
                var targetBase = targetSlot * InlineSlotCount;
                var sourceBase = sourceSlot * InlineSlotCount;
                var inline = Math.Min(count, InlineSlotCount);
                for (var i = 0; i < inline; i += 1)
                {
                    _ordinals[targetBase + i] = _ordinals[sourceBase + i];
                }

                if (count <= InlineSlotCount ||
                    _overflow is null ||
                    !_overflow.TryGetValue(sourceSlot, out var spill))
                {
                    _overflow?.Remove(targetSlot);
                    return;
                }

                // 溢出数组不可变（写入一律新建），故共享引用即可，无需深拷贝。
                _overflow[targetSlot] = spill;
            }

            /// <summary>
            /// 并集（对应 <c>OrBitSet</c>）：有序归并去重。
            ///
            /// **分配契约**：空、自合并、结果不变、可 Copy 的包含关系路径在稳态下**零堆分配**；
            /// 其余路径只分配**一份**精确长度的最终 spill（外加首次 <c>_overflow</c> 字典）。
            /// 原实现的 <c>merged</c>、<c>source</c>、<c>target</c> 三个中间数组已消除。
            ///
            /// **共享约束**：两侧 spill 在归并期间只读，**旧 spill 永不被写入**；
            /// 目标槽的 inline/count/overflow 全部在归并完成后一次性发布。
            /// </summary>
            internal void Union(int targetSlot, int sourceSlot)
            {
                var sourceCount = _counts[sourceSlot];
                if (sourceCount == 0)
                {
                    return;
                }

                if (targetSlot == sourceSlot)
                {
                    // 自合并：并集恒等于自身，且无需读取任何元素。
                    return;
                }

                var targetCount = _counts[targetSlot];
                if (targetCount == 0)
                {
                    Copy(targetSlot, sourceSlot);
                    return;
                }

                // 在任何写入之前捕获两侧的只读视图（count + inline + spill 引用）。
                // 两侧可能是不同槽但共享同一个 spill，故一律按只读输入处理。
                var target = CaptureView(targetSlot);
                var source = CaptureView(sourceSlot);

                // 第一遍：只计算并集基数，不写入任何目标状态。
                var unionCount = CountUnion(target, source);

                // 并集必为 target 的超集 ⇒ 基数不变即结果完全不变，可原样返回。
                if (unionCount == targetCount)
                {
                    return;
                }

                // 基数等于 source 基数 ⇒ target 是 source 的子集（且二者不等），Copy 即可，
                // 这同样保留不可变 spill 的共享引用。
                if (unionCount == sourceCount)
                {
                    Copy(targetSlot, sourceSlot);
                    return;
                }

                // 第二遍：只分配精确长度的最终 spill，把首元素写回目标内联槽，
                // 其余直接写新 spill。全程只读已捕获的旧输入，不调用会再复制一次的 WriteFrom。
                //
                // 走到这里必有 unionCount > targetCount 且 targetCount >= 1（上两个提前返回已排除
                // 相等与更小的情形），故 unionCount >= 2 > InlineSlotCount ⇒ 必然需要溢出数组。
                var spill = new int[unionCount - InlineSlotCount];
                MergeIntoUnion(target, source, targetSlot, spill);
                _counts[targetSlot] = unionCount;
                _overflow ??= new Dictionary<int, int[]>();
                _overflow[targetSlot] = spill;
            }

            /// <summary>
            /// 捕获槽位的只读视图：内联槽立即读成标量，spill 只取引用，不复制内容。
            /// </summary>
            private SetView CaptureView(int slot)
            {
                var count = _counts[slot];
                int[]? spill = null;
                if (count > InlineSlotCount &&
                    _overflow is not null &&
                    _overflow.TryGetValue(slot, out var existing))
                {
                    spill = existing;
                }

                var inline = count > 0 ? _ordinals[slot * InlineSlotCount] : 0;
                return new SetView(inline, count, spill);
            }

            /// <summary>有序归并去重，仅返回并集基数；不写入任何状态。</summary>
            private static int CountUnion(SetView left, SetView right)
            {
                var leftIndex = 0;
                var rightIndex = 0;
                var count = 0;
                while (leftIndex < left.Count && rightIndex < right.Count)
                {
                    var leftValue = left[leftIndex];
                    var rightValue = right[rightIndex];
                    if (leftValue < rightValue)
                    {
                        leftIndex += 1;
                    }
                    else if (rightValue < leftValue)
                    {
                        rightIndex += 1;
                    }
                    else
                    {
                        leftIndex += 1;
                        rightIndex += 1;
                    }

                    count += 1;
                }

                return count + (left.Count - leftIndex) + (right.Count - rightIndex);
            }

            /// <summary>
            /// 第二遍有序归并去重：首元素写入 <paramref name="targetSlot"/> 的内联槽，
            /// 其余写入 <paramref name="spill"/>。调用方已保证 <paramref name="spill"/> 长度恰为
            /// <c>unionCount - InlineSlotCount</c>。
            /// </summary>
            private void MergeIntoUnion(SetView left, SetView right, int targetSlot, int[] spill)
            {
                var ordinals = _ordinals;
                var baseIndex = targetSlot * InlineSlotCount;
                var written = 0;
                var leftIndex = 0;
                var rightIndex = 0;
                while (leftIndex < left.Count && rightIndex < right.Count)
                {
                    var leftValue = left[leftIndex];
                    var rightValue = right[rightIndex];
                    int value;
                    if (leftValue < rightValue)
                    {
                        value = leftValue;
                        leftIndex += 1;
                    }
                    else if (rightValue < leftValue)
                    {
                        value = rightValue;
                        rightIndex += 1;
                    }
                    else
                    {
                        value = leftValue;
                        leftIndex += 1;
                        rightIndex += 1;
                    }

                    WriteUnionElement(ordinals, baseIndex, spill, written, value);
                    written += 1;
                }

                while (leftIndex < left.Count)
                {
                    WriteUnionElement(ordinals, baseIndex, spill, written, left[leftIndex]);
                    written += 1;
                    leftIndex += 1;
                }

                while (rightIndex < right.Count)
                {
                    WriteUnionElement(ordinals, baseIndex, spill, written, right[rightIndex]);
                    written += 1;
                    rightIndex += 1;
                }
            }

            /// <summary>把并集的第 <paramref name="index"/> 个元素写到内联槽或最终 spill。</summary>
            private static void WriteUnionElement(
              int[] ordinals, int baseIndex, int[] spill, int index, int value)
            {
                if (index == 0)
                {
                    ordinals[baseIndex] = value;
                    return;
                }

                spill[index - 1] = value;
            }

            /// <summary>逐元素精确相等（对应 <c>BitSetEquals</c>）；近似比较会让 fixpoint 提前收敛。</summary>
            internal bool SetEquals(int leftSlot, int rightSlot)
            {
                var leftCount = _counts[leftSlot];
                if (leftCount != _counts[rightSlot])
                {
                    return false;
                }

                if (leftCount == 0)
                {
                    return true;
                }

                var leftBase = leftSlot * InlineSlotCount;
                var rightBase = rightSlot * InlineSlotCount;
                var inline = Math.Min(leftCount, InlineSlotCount);
                for (var i = 0; i < inline; i += 1)
                {
                    if (_ordinals[leftBase + i] != _ordinals[rightBase + i])
                    {
                        return false;
                    }
                }

                if (leftCount <= InlineSlotCount)
                {
                    return true;
                }

                if (_overflow is null ||
                    !_overflow.TryGetValue(leftSlot, out var leftSpill) ||
                    !_overflow.TryGetValue(rightSlot, out var rightSpill))
                {
                    return false;
                }

                return leftSpill.AsSpan().SequenceEqual(rightSpill);
            }

            /// <summary>
            /// 定义转移（对应 <c>ApplyDefinitionTransfer</c>）：
            /// 清除与 <paramref name="definedFact"/> 冲突的旧定义，再按序插入新定义。
            /// 单次转移**可以清位**（数值上可能变小），故 out 集并非逐位单调；
            /// fixpoint 的单调性来自 Kleene 混沌迭代不变式（设计 §3.4），不依赖此处。
            /// </summary>
            /// <remarks>
            /// <c>internal</c>：签名含 <c>DefinitionFact</c>，故该类型也一并放宽为
            /// <c>internal</c>（见 NLCPGBuilder.cs）。两者都仍是非公开类型，未新增公开 API。
            /// </remarks>
            internal void ApplyDefinitionTransfer(
              int slot,
              int definitionOrdinal,
              DefinitionFact definedFact,
              IReadOnlyList<DefinitionFact> definitionFactsByDefinitionOrdinal)
            {
                var count = _counts[slot];
                if (count == 0)
                {
                    Span<int> single = stackalloc int[1];
                    single[0] = definitionOrdinal;
                    WriteFrom(slot, single);
                    return;
                }

                Span<int> current = count <= StackBufferCapacity
                  ? stackalloc int[StackBufferCapacity]
                  : new int[count];
                count = ReadInto(slot, current);

                var kept = new int[count + 1];
                var keptCount = 0;
                var inserted = false;
                for (var i = 0; i < count; i += 1)
                {
                    var ordinal = current[i];
                    // 同序数自反：FactsConflict(f, f) 恒为 true，故显式跳过并在原位重插，
                    // 否则会被当成"删除后追加"，破坏升序。
                    if (ordinal == definitionOrdinal)
                    {
                        continue;
                    }

                    if (ordinal < definitionFactsByDefinitionOrdinal.Count &&
                        FactsConflict(definitionFactsByDefinitionOrdinal[ordinal], definedFact))
                    {
                        continue;
                    }

                    if (!inserted && ordinal > definitionOrdinal)
                    {
                        kept[keptCount] = definitionOrdinal;
                        keptCount += 1;
                        inserted = true;
                    }

                    kept[keptCount] = ordinal;
                    keptCount += 1;
                }

                if (!inserted)
                {
                    kept[keptCount] = definitionOrdinal;
                    keptCount += 1;
                }

                WriteFrom(slot, kept.AsSpan(0, keptCount));
            }

            /// <summary>把槽位元素按升序写入缓冲，返回元素个数。</summary>
            private int ReadInto(int slot, Span<int> destination)
            {
                var count = _counts[slot];
                var baseIndex = slot * InlineSlotCount;
                var inline = Math.Min(count, InlineSlotCount);
                for (var i = 0; i < inline; i += 1)
                {
                    destination[i] = _ordinals[baseIndex + i];
                }

                if (count > InlineSlotCount &&
                    _overflow is not null &&
                    _overflow.TryGetValue(slot, out var spill))
                {
                    spill.CopyTo(destination.Slice(InlineSlotCount));
                }

                return count;
            }

            /// <summary>用升序元素序列覆盖槽位；<paramref name="elements"/> 必须严格升序且无重复。</summary>
            private void WriteFrom(int slot, ReadOnlySpan<int> elements)
            {
                var count = elements.Length;
                _counts[slot] = count;
                var baseIndex = slot * InlineSlotCount;
                var inline = Math.Min(count, InlineSlotCount);
                for (var i = 0; i < inline; i += 1)
                {
                    _ordinals[baseIndex + i] = elements[i];
                }

                if (count <= InlineSlotCount)
                {
                    _overflow?.Remove(slot);
                    return;
                }

                var spill = new int[count - InlineSlotCount];
                elements.Slice(InlineSlotCount).CopyTo(spill);
                _overflow ??= new Dictionary<int, int[]>();
                _overflow[slot] = spill;
            }
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

        /// <summary>
        /// 补跨调用/属性访问的摘要数据流（D1：**逐文件**处理）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠ 本方法是**跨方法**分析：调用点与目标方法通常分处不同文件。
        /// 原实现取单一 <c>context.Graph</c>，即假设「调用点的图 == 目标方法的图」。
        /// 多文件下该假设不成立，而**跨文件的跨过程分析**是一个独立的设计决策
        /// （见执行文档 T4「非批次串行图写入者」一节的说明：需单独裁决）。
        /// </para>
        /// <para>
        /// 故此处采取**保守且与单文件逐字等价**的做法：逐文件处理，
        /// 用该文件自己的图同时解析调用点与目标节点。
        /// 单文件时 <c>context.Documents</c> 恒为「自身」一项 ⇒ 与改造前**完全相同**。
        /// 多文件下，跨文件调用的目标若不在本文件的图里，
        /// <c>GetOrCreateMethodReturnNode</c> 会在**本文件**图内物化一个节点——
        /// 这是「每文件图自洽」的语义，且不会跨图交叉链接。
        /// </para>
        /// </remarks>
        private void AddCallArgumentAndReturnDataFlow(NLCPGBuildContext context)
        {
            foreach (var document in context.Documents)
            {
                AddCallArgumentAndReturnDataFlowForDocument(document);
            }
        }

        private void AddCallArgumentAndReturnDataFlowForDocument(NLCPGBuildContext context)
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
                    graph.AddEdge(returnNode, callSiteNode.Value, NLCPGEdgeKind.DataFlow);
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
                graph.AddEdge(returnNode, callSiteNode.Value, NLCPGEdgeKind.DataFlow);
                graph.AddEdge(callSiteNode.Value, propertyNode, NLCPGEdgeKind.DataFlow);
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
                graph.AddEdge(callSiteNode.Value, propertyNode, NLCPGEdgeKind.DataFlow);
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
                    graph.AddEdge(valueNode, callSiteNode.Value, NLCPGEdgeKind.DataFlow);
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
            // SymbolId 含 ToDisplayString/Locations 查询，结果在两个字段里必须一致；
            // 原先调用两次会重复付出同一份代价，故此处只计算一次。
            var symbolId = SymbolId(symbol);
            return new DefinitionFact(symbolId, null, symbol.Kind.ToString(), symbolId);
        }

        private static DefinitionFact DefinitionFactForParameter(IParameterSymbol parameter)
        {
            var parameterId = SymbolId(parameter);
            return new DefinitionFact(parameterId, null, "parameter", parameterId);
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
