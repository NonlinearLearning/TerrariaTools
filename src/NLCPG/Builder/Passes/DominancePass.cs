using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Collections;
using System.Numerics;

namespace NLCPG.Builder.Passes
{

    internal sealed class DominancePass : INLCPGPass
    {
        internal static DominancePass Instance { get; } = new();

        private DominancePass()
        {
        }

        public string Name => nameof(DominancePass);

        // 触发支配关系 pass，并缓存控制依赖后续会复用的 overlay。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            // Pass 实例只负责转发，具体的 CFG 推导和图写入集中在 builder，避免状态分散。
            builder.RunDominancePass(context);
        }
    }

}

namespace NLCPG.Builder
{

    public sealed partial class NLCPGBuilder
    {
        private sealed class BlockBitSet : IReadOnlySet<int>
        {
            private readonly int _capacity;
            private readonly ulong[] _words;

            internal BlockBitSet(int capacity)
            {
                // 每个 basic block 用一个 bit 表示；向上取整到 ulong 边界，保证集合运算按机器字执行。
                ArgumentOutOfRangeException.ThrowIfNegative(capacity);
                _capacity = capacity;
                _words = new ulong[(capacity + 63) / 64];
            }

            private BlockBitSet(int capacity, ulong[] words)
            {
                // 私有构造器接管已复制的存储，仅由 Clone 使用以保持容量与内容一致。
                _capacity = capacity;
                _words = words;
            }

            public int Count
            {
                get
                {
                    // Count 只在调用方需要集合大小时计算，逐字 PopCount 避免为该结果维护额外计数器。
                    var count = 0;
                    foreach (var word in _words)
                    {
                        count += BitOperations.PopCount(word);
                    }

                    return count;
                }
            }

            internal BlockBitSet Clone()
            {
                // 不动原集合地复制当前数据流迭代状态，供交集计算产生下一轮候选。
                return new BlockBitSet(_capacity, (ulong[])_words.Clone());
            }

            internal void Set(int ordinal)
            {
                // 先校验 CFG 序号，再定位所属机器字及其中的 bit。
                ValidateOrdinal(ordinal);
                _words[ordinal / 64] |= 1UL << (ordinal % 64);
            }

            internal void AndWith(BlockBitSet other)
            {
                // 支配集合的交集等价于逐机器字按位与，容量不同则不存在可比较关系。
                ValidateCompatible(other);
                for (var index = 0; index < _words.Length; index += 1)
                {
                    _words[index] &= other._words[index];
                }
            }

            internal bool BitwiseEquals(BlockBitSet other)
            {
                // 固定点迭代以底层位图相等作为收敛条件，避免枚举集合带来的额外分配。
                ValidateCompatible(other);
                for (var index = 0; index < _words.Length; index += 1)
                {
                    if (_words[index] != other._words[index])
                    {
                        return false;
                    }
                }

                return true;
            }

            // 判断位图中是否包含指定 basic block 序号。
            public bool Contains(int item)
            {
                // 查询越界时按集合语义返回 false，合法序号再读取相应 bit。
                if (item < 0 || item >= _capacity)
                {
                    return false;
                }

                return (_words[item / 64] & (1UL << (item % 64))) != 0;
            }

            // 以升序枚举当前位图中已设置的 basic block 序号。
            public IEnumerator<int> GetEnumerator()
            {
                // 逐个清除最低有效位，只枚举已设置的 block 序号而不扫描每个 bit。
                for (var wordIndex = 0; wordIndex < _words.Length; wordIndex += 1)
                {
                    var remaining = _words[wordIndex];
                    while (remaining != 0)
                    {
                        var bitIndex = BitOperations.TrailingZeroCount(remaining);
                        var ordinal = (wordIndex * 64) + bitIndex;
                        if (ordinal < _capacity)
                        {
                            yield return ordinal;
                        }

                        remaining &= remaining - 1;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                // 非泛型集合接口复用泛型枚举器，保证两种枚举路径的顺序一致。
                return GetEnumerator();
            }

            // 判断当前集合是否为给定集合的真子集。
            public bool IsProperSubsetOf(IEnumerable<int> other)
            {
                // 真子集要求包含关系成立且两边不完全相同。
                return IsSubsetOf(other) && !SetEquals(other);
            }

            // 判断当前集合是否为给定集合的真超集。
            public bool IsProperSupersetOf(IEnumerable<int> other)
            {
                // 真超集同样在普通超集判断之外排除相等情况。
                return IsSupersetOf(other) && !SetEquals(other);
            }

            // 判断当前集合是否完全包含于给定集合。
            public bool IsSubsetOf(IEnumerable<int> other)
            {
                // 相同实现可用位图一次比较；一般 IEnumerable 退化为集合查询以保留接口语义。
                if (other is BlockBitSet otherBitSet)
                {
                    ValidateCompatible(otherBitSet);
                    for (var index = 0; index < _words.Length; index += 1)
                    {
                        if ((_words[index] & ~otherBitSet._words[index]) != 0)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                var otherSet = other.ToHashSet();
                return this.All(otherSet.Contains);
            }

            // 判断当前集合是否完全覆盖给定集合。
            public bool IsSupersetOf(IEnumerable<int> other)
            {
                // 位图路径检查 other 中是否存在本集合没有的 bit；泛型路径逐项短路。
                if (other is BlockBitSet otherBitSet)
                {
                    ValidateCompatible(otherBitSet);
                    for (var index = 0; index < _words.Length; index += 1)
                    {
                        if ((otherBitSet._words[index] & ~_words[index]) != 0)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                foreach (var ordinal in other)
                {
                    if (!Contains(ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }

            // 判断当前集合与给定集合是否存在交集。
            public bool Overlaps(IEnumerable<int> other)
            {
                // 位图路径用按位与快速判断是否存在交集，其他集合维持惰性短路。
                if (other is BlockBitSet otherBitSet)
                {
                    ValidateCompatible(otherBitSet);
                    for (var index = 0; index < _words.Length; index += 1)
                    {
                        if ((_words[index] & otherBitSet._words[index]) != 0)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                return other.Any(Contains);
            }

            // 判断当前集合与给定集合是否拥有相同成员。
            public bool SetEquals(IEnumerable<int> other)
            {
                // 对同类直接复用字级比较；对外部枚举先规范化重复项后再比较基数和成员。
                if (other is BlockBitSet otherBitSet)
                {
                    return BitwiseEquals(otherBitSet);
                }

                var otherSet = other.ToHashSet();
                return Count == otherSet.Count && this.All(otherSet.Contains);
            }

            private void ValidateCompatible(BlockBitSet other)
            {
                // 两张位图只有容量一致时，索引才表示同一组 CFG block。
                if (_capacity != other._capacity)
                {
                    throw new InvalidOperationException("BlockBitSet capacity mismatch.");
                }
            }

            private void ValidateOrdinal(int ordinal)
            {
                // 写入越界会破坏位图索引，因此在所有 Set 调用处统一拒绝。
                if (ordinal < 0 || ordinal >= _capacity)
                {
                    throw new ArgumentOutOfRangeException(nameof(ordinal));
                }
            }
        }

        /// <summary>
        /// **按文件**保存的支配覆盖层（D1）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠ 原为**扁平 <c>List</c>**，而 <c>ControlDependencePass.cs:111</c> 用
        /// <c>_dominanceOverlays[batch.StableOrder]</c> 直接**下标**它。
        /// <c>StableOrder</c> 是**文件内局部**序号（见 <c>CpgWorkItem</c> 的
        /// <c>StableOrder</c>/<c>ShardOrder</c> 双轨设计）⇒ 跨文件批次下该下标必然错配：
        /// 或用错文件的覆盖层，或越界。
        /// </para>
        /// <para>
        /// 改为**按文件分桶**后，下标域重新回到「单文件局部」：
        /// 每个文件各自的 <c>List</c> 用该文件自己的 <c>StableOrder</c> 下标，
        /// 与单文件路径**逐字同构**。这比引入一个全局序号的代价小得多，
        /// 且不需要改动 <c>StableOrder</c> 的既有语义（那会破坏 6 处既有下标/键用法）。
        /// </para>
        /// <para>
        /// 键的遍历一律走 <see cref="OverlayFilePaths"/>（登记序），不用字典枚举序。
        /// </para>
        /// </remarks>
        private readonly Dictionary<string, List<DominanceMethodOverlay>> _dominanceOverlaysByFile =
          new(StringComparer.Ordinal);

        private readonly List<string> _overlayFilePaths = new();

        /// <summary>有覆盖层的文件，按**首次登记序**（确定性）。</summary>
        private IReadOnlyList<string> OverlayFilePaths => _overlayFilePaths;

        private void ClearDominanceOverlays()
        {
            _dominanceOverlaysByFile.Clear();
            _overlayFilePaths.Clear();
        }

        private void AddDominanceOverlay(string sourceFilePath, DominanceMethodOverlay overlay)
        {
            if (!_dominanceOverlaysByFile.TryGetValue(sourceFilePath, out var overlays))
            {
                overlays = new List<DominanceMethodOverlay>();
                _dominanceOverlaysByFile[sourceFilePath] = overlays;
                _overlayFilePaths.Add(sourceFilePath);
            }

            overlays.Add(overlay);
        }

        private IReadOnlyList<DominanceMethodOverlay> DominanceOverlaysFor(string sourceFilePath)
        {
            return _dominanceOverlaysByFile.TryGetValue(sourceFilePath, out var overlays)
              ? overlays
              : Array.Empty<DominanceMethodOverlay>();
        }

        /// <summary>全部覆盖层的总数（跨文件）——保持改造前「Count」的可观测语义。</summary>
        private int DominanceOverlayCount => _dominanceOverlaysByFile.Sum(entry => entry.Value.Count);

        private sealed record DominanceMethodOverlay(
            ControlFlowGraph ControlFlowGraph,
            IReadOnlyDictionary<int, BlockBitSet> DominatorsByBlockOrdinal,
            IReadOnlyDictionary<int, BlockBitSet> PostDominatorsByBlockOrdinal,
            IReadOnlyDictionary<int, int?> ImmediatePostDominatorByBlockOrdinal,
            IReadOnlyDictionary<int, IReadOnlyList<NLCPGNode>> NodesByBlockOrdinal,
            IReadOnlyDictionary<int, NLCPGNode?> ControlNodesByBlockOrdinal);

        private sealed record DominanceRootResult(
          string SourceFilePath,
          int Order,
          DominanceMethodOverlay Overlay,
          LocalCpgFragment Fragment);

        private sealed record DominanceWorkBatchResult(
          long BatchId,
          int StableOrder,
          IReadOnlyList<DominanceRootResult> Roots);

        internal void RunDominancePass(NLCPGBuildContext context)
        {
            // G0-P R-3：本阶段的规划已在【执行相位之前】完成并登记（见 PlanStagesBeforeExecution）。
            // 这里只消费既有 plan。
            var plan = TakeRecordedStagePlan(StageDependencyTable.Stage.Dominance);
            if (plan is null)
            {
                // 规划相位【只在无操作根时】返回 null（见 PlanDominanceStage）。
                // 若此处仍有操作根，说明规划相位没有覆盖本阶段 —— fail-closed，
                // 而不是静默跳过整阶段的支配边（正是 G0-P 要消灭的失效形态）。
                // ⚠ D1：必须检查**全部文档**（见 HasAnyOperationRootAcrossDocuments）。
                if (HasAnyOperationRootAcrossDocuments(context))
                {
                    throw new InvalidOperationException(
                      "Dominance 阶段被请求执行，但规划相位没有登记它的 plan（G0-P R-3）。"
                      + "规划必须在任何 worker 启动之前完成；此处不重新规划，以免把规划时点拉回执行相位。");
                }

                // 无操作根：与拆分前一致——清空上次构建的 overlay 后返回。
                ClearDominanceOverlays();
                return;
            }

            CommitDominanceStage(context, plan);
        }

        /// <summary>
        /// G0-P **R-3** 规划步：**只读**——算出批次，不触碰图。
        /// <para>
        /// <b>为何可静态规划（源码确证）：</b>批次来自 <c>AssembleWorkBatches(context)</c>，
        /// 其唯一输入是 <c>GetOperationRootPlans</c> 与无状态批次构造器；
        /// 本阶段**真正**依赖运行期状态的是 <c>AnalyzeDominanceRoot</c>（读各方法 CFG 与已物化节点），
        /// 而那属于**计算**相位，不属于规划。故规划本身可前移。
        /// </para>
        /// </summary>
        private StagePlan? PlanDominanceStage(NLCPGBuildContext context)
        {
            // ⚠ D1：判据必须是**全部文档**是否有操作根（见 HasAnyOperationRootAcrossDocuments）。
            if (!HasAnyOperationRootAcrossDocuments(context))
            {
                return null;
            }

            return new StagePlan(
              StageDependencyTable.Stage.Dominance,
              // D1：跨文件聚合装箱（单文件时与 AssembleWorkBatches 逐字相同）。
              AssembleWorkBatchesAcrossDocuments(context));
        }

        /// <summary>
        /// G0-P **R-3** 提交步：消费 plan，执行 worker 并归并（**唯一写图者**）。
        /// </summary>
        private void CommitDominanceStage(NLCPGBuildContext context, StagePlan plan)
        {
            ClearDominanceOverlays();
            // D1：**逐文件**建立 order→rootPlan 索引。批次可跨文件，而 Order 是文件内局部序号，
            //   故必须按 item 自己的文件查表；单一全局表在跨文件下会把 B 的序号解析成 A 的方法。
            var rootsByFileAndOrder = new Dictionary<string, Dictionary<int, OperationRootPlan>>(StringComparer.Ordinal);
            foreach (var document in context.Documents)
            {
                rootsByFileAndOrder[document.FilePath] = GetOperationRootPlans(document.Root, document.SemanticModel)
                  .ToDictionary(root => root.Order);
            }

            var workBatches = plan.Batches;
            var batchResults = _workBatchExecutor.ExecuteAsync(
              workBatches,
              (batch, _, _) =>
              {
                  var roots = batch.Items
                    .OrderBy(item => item.StableOrder)
                    .Select(item =>
                    {
                        // ⚠ 用 item 自己文件的 context 与 rootPlan 索引（D1）。
                        var document = context.ResolveDocument(item.SourceFilePath);
                        return rootsByFileAndOrder.TryGetValue(item.SourceFilePath, out var rootsByOrder) &&
                               rootsByOrder.TryGetValue(item.StableOrder, out var rootPlan)
                          ? AnalyzeDominanceRoot(document, rootPlan)
                          : null;
                    })
                    .Where(result => result is not null)
                    .Select(result => result!)
                    .ToArray();
                  return new DominanceWorkBatchResult(batch.BatchId, batch.StableOrder, roots);
              },
              stageId: CpgWorkBatchPerformanceStageId.Dominance).GetAwaiter().GetResult();
            var roots = batchResults
              .SelectMany(result => result.Roots)
              .OrderBy(result => result.Order)
              .ToArray();
            // ⚠ 覆盖层**按文件分桶**：ControlDependencePass 用 batch.StableOrder（文件内局部序号）
            //   直接下标它，扁平列表在跨文件批次下必然错配（D1）。
            foreach (var result in roots)
            {
                AddDominanceOverlay(result.SourceFilePath, result.Overlay);
            }

            // D1：按 fragment 的来源文件路由到各自的图，而非一律写 context.Graph。
            ReduceFragments(context, roots.Select(result => result.Fragment));
        }

        /// <summary>
        /// 无 plan 时的退化路径：与拆分前一致——清空 overlay 后直接返回。
        /// <para>
        /// 刻意<b>不</b>在此重新规划：重新规划会把规划时点悄悄拉回执行相位，
        /// 正是 <see cref="StagePlan"/> 要防的形态。
        /// </para>
        /// </summary>
        private void RunDominancePassWithoutPlan(NLCPGBuildContext context)
        {
            ClearDominanceOverlays();
        }

        // 保留逐根路径作为迁移期间的语义对照实现。
        private void RunDominancePassOrderedCompatibility(NLCPGBuildContext context)
        {
            // 覆盖层只服务本次构建；先清空上一次方法的 CFG 派生状态。
            ClearDominanceOverlays();

            // 每个方法独立构造 CFG，失败或非方法根节点不参与支配关系计算。
            foreach (var rootPlan in GetOperationRootPlans(context.Root, context.SemanticModel))
            {
                if (GetOperationRoot(context, rootPlan.BodySyntax) is not IBlockOperation methodBlock ||
                    !IsMethodRootBlock(methodBlock) ||
                    rootPlan.OwningMethod is not IMethodSymbol methodSymbol)
                {
                    continue;
                }

                var controlFlowGraph = CreateControlFlowGraph(methodBlock);
                if (controlFlowGraph is null)
                {
                    continue;
                }
                // 建立 block 到已物化 CPG 节点的确定性映射，并据此计算前驱、后继和两类支配关系。
                var nodesByBlockOrdinal = MapNodesByBlockOrdinal(controlFlowGraph, methodSymbol, context.Graph);
                var controlNodesByBlockOrdinal = MapControlNodesByBlockOrdinal(controlFlowGraph, nodesByBlockOrdinal);
                var blockBitSetCapacity = controlFlowGraph.Blocks.Max(block => block.Ordinal) + 1;
                var successorsByBlockOrdinal = BuildSuccessorsByBlockOrdinal(controlFlowGraph, blockBitSetCapacity);
                var predecessorsByBlockOrdinal = ReverseNeighbors(successorsByBlockOrdinal);
                var entryOrdinal = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Entry).Ordinal;
                var exitOrdinal = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Exit).Ordinal;
                var dominators = CalculateDominators(
                    successorsByBlockOrdinal,
                    predecessorsByBlockOrdinal,
                    entryOrdinal);
                var postDominators = CalculateDominators(
                    predecessorsByBlockOrdinal,
                    successorsByBlockOrdinal,
                    exitOrdinal);
                var immediatePostDominators = CalculateImmediatePostDominators(postDominators);

                // 图边供查询使用，overlay 保留 CFG 与立即后支配者，供后续控制依赖 pass 复用。
                AddOverlayEdges(nodesByBlockOrdinal, dominators, NLCPGEdgeKind.Dominates, context.Graph);
                AddPostDominanceEdges(nodesByBlockOrdinal, postDominators, context.Graph);
                AddDominanceOverlay(context.FilePath, new DominanceMethodOverlay(
                    controlFlowGraph,
                    dominators,
                    postDominators,
                    immediatePostDominators,
                    nodesByBlockOrdinal,
                    controlNodesByBlockOrdinal));
            }
        }

        private DominanceRootResult? AnalyzeDominanceRoot(
          NLCPGBuildContext context,
          OperationRootPlan rootPlan)
        {
            if (GetOperationRoot(context, rootPlan.BodySyntax) is not IBlockOperation methodBlock ||
                !IsMethodRootBlock(methodBlock) ||
                rootPlan.OwningMethod is not IMethodSymbol methodSymbol)
            {
                return null;
            }

            var controlFlowGraph = CreateControlFlowGraph(methodBlock);
            if (controlFlowGraph is null)
            {
                return null;
            }

            var localGraph = new NLCPGGraph(
              identityFactory: context.Graph.IdentityFactory,
              stringInterner: context.Graph.StringTable);
            var nodesByBlockOrdinal = MapNodesByBlockOrdinal(controlFlowGraph, methodSymbol, localGraph);
            var controlNodesByBlockOrdinal = MapControlNodesByBlockOrdinal(controlFlowGraph, nodesByBlockOrdinal);
            foreach (var node in nodesByBlockOrdinal.Values.SelectMany(nodes => nodes))
            {
                localGraph.AddNode(node);
            }
            var blockBitSetCapacity = controlFlowGraph.Blocks.Max(block => block.Ordinal) + 1;
            var successorsByBlockOrdinal = BuildSuccessorsByBlockOrdinal(controlFlowGraph, blockBitSetCapacity);
            var predecessorsByBlockOrdinal = ReverseNeighbors(successorsByBlockOrdinal);
            var entryOrdinal = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Entry).Ordinal;
            var exitOrdinal = controlFlowGraph.Blocks.Single(block => block.Kind == BasicBlockKind.Exit).Ordinal;
            var dominators = CalculateDominators(
              successorsByBlockOrdinal,
              predecessorsByBlockOrdinal,
              entryOrdinal);
            var postDominators = CalculateDominators(
              predecessorsByBlockOrdinal,
              successorsByBlockOrdinal,
              exitOrdinal);
            var immediatePostDominators = CalculateImmediatePostDominators(postDominators);
            AddOverlayEdges(nodesByBlockOrdinal, dominators, NLCPGEdgeKind.Dominates, localGraph);
            AddPostDominanceEdges(nodesByBlockOrdinal, postDominators, localGraph);
            var overlay = new DominanceMethodOverlay(
              controlFlowGraph,
              dominators,
              postDominators,
              immediatePostDominators,
              nodesByBlockOrdinal,
              controlNodesByBlockOrdinal);

            localGraph.FreezeQueryIndex();
            var nodesById = localGraph.Nodes
              .Where(node => node.NodeId.HasValue)
              .ToDictionary(node => node.NodeId!.Value);
            var descriptors = localGraph.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();
            var edges = localGraph.Edges
              .Select(edge =>
              {
                  var source = nodesById[edge.SourceNodeId];
                  var target = nodesById[edge.TargetNodeId];
                  return new CpgEdgeCandidate(
                    source.StableAnchor!.Value,
                    target.StableAnchor!.Value,
                    edge.Kind,
                    edge.StructuredLabel,
                    edge.ContextId,
                    edge.CallSiteContext);
              })
              .ToArray();
            var fragment = new LocalCpgFragment(
              rootPlan.Order,
              context.FilePath,
              rootPlan.Order,
              descriptors,
              edges,
              Array.Empty<CpgMethodSummary>(),
              Array.Empty<CpgBoundaryReference>(),
              new CpgFragmentMetrics(
                0,
                0,
                descriptors.Length,
                edges.Length,
                descriptors.Length,
                edges.Length,
                checked(descriptors.Length * 64 + edges.Length * 48)),
              Array.Empty<CpgDiagnostic>());
            return new DominanceRootResult(context.FilePath, rootPlan.Order, overlay, fragment);
        }

        private Dictionary<int, IReadOnlyList<NLCPGNode>> MapNodesByBlockOrdinal(ControlFlowGraph controlFlowGraph, IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            // 将 Roslyn block 的入口、出口及所有操作后代投影到既有 CPG 节点。
            var nodesByBlockOrdinal = new Dictionary<int, IReadOnlyList<NLCPGNode>>();
            foreach (var block in controlFlowGraph.Blocks)
            {
                var nodes = new HashSet<NLCPGNode>();
                // Entry/Exit 没有普通操作，显式映射到方法边界节点以保持控制关系完整。
                if (block.Kind == BasicBlockKind.Entry)
                {
                    nodes.Add(GetOrCreateMethodEntryNode(methodSymbol, graph));
                }
                else if (block.Kind == BasicBlockKind.Exit)
                {
                    nodes.Add(GetOrCreateMethodExitNode(methodSymbol, graph));
                }

                // block 内操作和分支条件都可能产生图节点，二者均需纳入投影。
                foreach (var operation in block.Operations)
                {
                    foreach (var descendant in operation.DescendantsAndSelf())
                    {
                        AddMappedOperationNode(descendant, nodes, graph);
                    }
                }

                if (block.BranchValue is not null)
                {
                    foreach (var descendant in block.BranchValue.DescendantsAndSelf())
                    {
                        AddMappedOperationNode(descendant, nodes, graph);
                    }
                }

                // 以稳定 NodeId 与名称排序，防止 HashSet 枚举次序影响边提交顺序。
                nodesByBlockOrdinal[block.Ordinal] = nodes
                    .OrderBy(node => node.NodeId)
                    .ThenBy(node => graph.ResolveFullName(node), StringComparer.Ordinal)
                    .ToArray();
            }

            return nodesByBlockOrdinal;
        }

        private static ControlFlowGraph? CreateControlFlowGraph(IBlockOperation methodBlock)
        {
            // Roslyn 仅允许从方法体或构造函数体创建 CFG，其余 block 没有独立控制流边界。
            return methodBlock.Parent switch
            {
                IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody),
                IConstructorBodyOperation constructorBody => ControlFlowGraph.Create(constructorBody),
                _ => null,
            };
        }

        private void AddMappedOperationNode(IOperation operation, ISet<NLCPGNode> nodes, NLCPGGraph graph)
        {
            // 复用 operation-node 缓存，保证同一 Roslyn 操作在多个 CFG 位置仍指向同一 CPG 节点。
            nodes.Add(GetOrCreateOperationNode(operation, graph));
        }

        private static Dictionary<int, NLCPGNode?> MapControlNodesByBlockOrdinal(ControlFlowGraph controlFlowGraph, IReadOnlyDictionary<int, IReadOnlyList<NLCPGNode>> nodesByBlockOrdinal)
        {
            // 为每个 block 选一个最能表达分支控制的节点，后续控制依赖边从此节点发出。
            var controlNodesByBlockOrdinal = new Dictionary<int, NLCPGNode?>();
            foreach (var block in controlFlowGraph.Blocks)
            {
                // 优先二元条件和条件操作；跨度较大的节点与稳定 ID 用于消解并列候选。
                controlNodesByBlockOrdinal[block.Ordinal] = nodesByBlockOrdinal[block.Ordinal]
                    .OrderBy(node => GetControlNodeKindPriority(node.Kind))
                    .ThenByDescending(node => (node.SpanEnd ?? int.MinValue) - (node.SpanStart ?? int.MaxValue))
                    .ThenBy(node => node.NodeId)
                    .FirstOrDefault();
            }

            return controlNodesByBlockOrdinal;
        }

        private static int GetControlNodeKindPriority(NLCPGNodeKind kind)
        {
            // 数值越小代表越适合作为控制谓词，其余节点只在没有专门谓词时兜底。
            return kind switch
            {
                NLCPGNodeKind.OpBinary => 1,
                NLCPGNodeKind.OpConditional => 2,
                NLCPGNodeKind.Operation => 3,
                _ => 4
            };
        }

        private static Dictionary<int, BlockBitSet> BuildSuccessorsByBlockOrdinal(ControlFlowGraph controlFlowGraph, int blockBitSetCapacity)
        {
            // 预建所有 block 的空集合，再收集普通落空和条件跳转两类 Roslyn 后继。
            var successorsByBlockOrdinal = controlFlowGraph.Blocks.ToDictionary(
                block => block.Ordinal,
                _ => new BlockBitSet(blockBitSetCapacity));
            foreach (var block in controlFlowGraph.Blocks)
            {
                var successors = successorsByBlockOrdinal[block.Ordinal];
                AddDestination(block.FallThroughSuccessor, successors);
                AddDestination(block.ConditionalSuccessor, successors);
            }

            return successorsByBlockOrdinal;
        }

        private static void AddDestination(ControlFlowBranch? branch, BlockBitSet successors)
        {
            // 缺失目的地代表终止分支，不写入不存在的 CFG block。
            if (branch?.Destination is not null)
            {
                successors.Set(branch.Destination.Ordinal);
            }
        }

        private static Dictionary<int, BlockBitSet> ReverseNeighbors(IReadOnlyDictionary<int, BlockBitSet> successorsByBlockOrdinal)
        {
            // 将后继关系反转为前驱关系，供支配集合对所有可达前驱取交集。
            var blockBitSetCapacity = successorsByBlockOrdinal.Keys.Max() + 1;
            var predecessors = successorsByBlockOrdinal.Keys.ToDictionary(
                ordinal => ordinal,
                _ => new BlockBitSet(blockBitSetCapacity));
            foreach (var (sourceOrdinal, successors) in successorsByBlockOrdinal)
            {
                foreach (var targetOrdinal in successors)
                {
                    predecessors[targetOrdinal].Set(sourceOrdinal);
                }
            }

            return predecessors;
        }

        private static IReadOnlyDictionary<int, BlockBitSet> CalculateDominators(IReadOnlyDictionary<int, BlockBitSet> successorsByBlockOrdinal, IReadOnlyDictionary<int, BlockBitSet> predecessorsByBlockOrdinal, int rootOrdinal)
        {
            // 同一实现同时计算支配和后支配：调用方只需交换图方向与根节点。
            var blockBitSetCapacity = successorsByBlockOrdinal.Keys.Max() + 1;
            var reachable = CalculateReversePostOrder(successorsByBlockOrdinal, rootOrdinal);
            var reachableSet = new BlockBitSet(blockBitSetCapacity);
            foreach (var ordinal in reachable)
            {
                reachableSet.Set(ordinal);
            }

            // 初始时根仅支配自己，可达非根暂定为所有可达 block；不可达节点保持自身集合。
            var dominators = successorsByBlockOrdinal.Keys.ToDictionary(
                ordinal => ordinal,
                ordinal => ordinal == rootOrdinal
                    ? CreateSingletonBitSet(blockBitSetCapacity, ordinal)
                    : reachableSet.Contains(ordinal)
                        ? reachableSet.Clone()
                        : CreateSingletonBitSet(blockBitSetCapacity, ordinal));

            // 反复对前驱集合求交直到无变化，得到经典的迭代数据流固定点。
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var ordinal in reachable.Where(ordinal => ordinal != rootOrdinal))
                {
                    var reachablePredecessors = predecessorsByBlockOrdinal[ordinal]
                        .Where(reachableSet.Contains)
                        .OrderBy(value => value)
                        .ToArray();
                    if (reachablePredecessors.Length == 0)
                    {
                        continue;
                    }

                    var intersection = dominators[reachablePredecessors[0]].Clone();
                    foreach (var predecessor in reachablePredecessors.Skip(1))
                    {
                        intersection.AndWith(dominators[predecessor]);
                    }

                    intersection.Set(ordinal);
                    if (!intersection.BitwiseEquals(dominators[ordinal]))
                    {
                        dominators[ordinal] = intersection;
                        changed = true;
                    }
                }
            }

            return dominators;
        }

        private static IReadOnlyList<int> CalculateReversePostOrder(IReadOnlyDictionary<int, BlockBitSet> successorsByBlockOrdinal, int rootOrdinal)
        {
            // 深度优先生成后序再反转，确保固定点计算总按可达且稳定的顺序遍历。
            var visited = new HashSet<int>();
            var postOrder = new List<int>();
            Visit(rootOrdinal);
            postOrder.Reverse();
            return postOrder;

            void Visit(int ordinal)
            {
                // 已访问节点直接返回，天然处理 CFG 回边和循环。
                if (!visited.Add(ordinal))
                {
                    return;
                }

                foreach (var successor in successorsByBlockOrdinal[ordinal])
                {
                    Visit(successor);
                }

                postOrder.Add(ordinal);
            }
        }

        private static IReadOnlyDictionary<int, int?> CalculateImmediatePostDominators(IReadOnlyDictionary<int, BlockBitSet> postDominatorsByBlockOrdinal)
        {
            // 在严格后支配者中选集合最深的一个；它就是控制依赖遍历的停止边界。
            var immediatePostDominators = new Dictionary<int, int?>();
            foreach (var (ordinal, postDominators) in postDominatorsByBlockOrdinal)
            {
                immediatePostDominators[ordinal] = postDominators
                    .Where(candidate => candidate != ordinal)
                    .OrderByDescending(candidate => postDominatorsByBlockOrdinal[candidate].Count)
                    .ThenBy(candidate => candidate)
                    .Select(candidate => (int?)candidate)
                    .FirstOrDefault();
            }

            return immediatePostDominators;
        }

        private static void AddOverlayEdges(IReadOnlyDictionary<int, IReadOnlyList<NLCPGNode>> nodesByBlockOrdinal, IReadOnlyDictionary<int, BlockBitSet> relationsByTargetBlockOrdinal, NLCPGEdgeKind edgeKind, NLCPGGraph graph)
        {
            // 将 block 粒度关系展开为节点笛卡尔积，排除自身关系以避免无意义自环。
            foreach (var (targetOrdinal, sourceOrdinals) in relationsByTargetBlockOrdinal)
            {
                var targetNodes = nodesByBlockOrdinal[targetOrdinal];
                if (targetNodes.Count == 0)
                {
                    continue;
                }

                foreach (var sourceOrdinal in sourceOrdinals)
                {
                    if (sourceOrdinal == targetOrdinal)
                    {
                        continue;
                    }

                    var sourceNodes = nodesByBlockOrdinal[sourceOrdinal];
                    if (sourceNodes.Count == 0)
                    {
                        continue;
                    }

                    graph.AddKnownNodeCartesianEdges(sourceNodes, targetNodes, edgeKind);
                }
            }
        }

        private static void AddPostDominanceEdges(IReadOnlyDictionary<int, IReadOnlyList<NLCPGNode>> nodesByBlockOrdinal, IReadOnlyDictionary<int, BlockBitSet> postDominatorsByBlockOrdinal, NLCPGGraph graph)
        {
            // 后支配边方向从原 block 指向其保证经过的后继 block，语义与 Dominates 相反。
            foreach (var (sourceOrdinal, postDominatorOrdinals) in postDominatorsByBlockOrdinal)
            {
                foreach (var targetOrdinal in postDominatorOrdinals.Where(targetOrdinal => targetOrdinal != sourceOrdinal))
                {
                    foreach (var sourceNode in nodesByBlockOrdinal[sourceOrdinal])
                    {
                        foreach (var targetNode in nodesByBlockOrdinal[targetOrdinal])
                        {
                            graph.AddEdge(sourceNode, targetNode, NLCPGEdgeKind.PostDominates);
                        }
                    }
                }
            }
        }

        private static BlockBitSet CreateSingletonBitSet(int blockBitSetCapacity, int ordinal)
        {
            // 为根和不可达节点构造只含自身的初始集合。
            var singleton = new BlockBitSet(blockBitSetCapacity);
            singleton.Set(ordinal);
            return singleton;
        }
    }

}
