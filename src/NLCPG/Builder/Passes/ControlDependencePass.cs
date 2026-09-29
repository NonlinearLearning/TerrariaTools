using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{

    internal sealed class ControlDependencePass : INLCPGPass
    {
        internal static ControlDependencePass Instance { get; } = new();

        private ControlDependencePass()
        {
        }

        public string Name => nameof(ControlDependencePass);

        // 触发控制依赖 pass，把 dominance overlay 写回图边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunControlDependencePass(context);
        }
    }

}

namespace NLCPG.Builder
{

    public sealed partial class NLCPGBuilder
    {
        internal void RunControlDependencePass(NLCPGBuildContext context)
        {
            // G0-P R-3：显式分为【只读规划】与【写图提交】两步。
            //
            // ⚠ 与本轮已前移的 3 个阶段（ControlFlow/MemberAccess/Dominance）**不同**：
            //   本阶段的规划**读不到**足以提前决策的输入——它依赖 Dominance **运行期**填充的
            //   `_dominanceOverlays`（plan 的规模就是该字段的 Count）。
            //   故规划**留在**执行相位内，但把结果登记为
            //   `DeferredUntilRuntimeInputs`，使"没能前移"成为**可观测的声明**：
            //   它会出现在 LastStagePlans，却**不会**出现在 PlanSnapshotAtEndOfPlanningPhase。
            var plan = PlanControlDependenceStage(context);
            if (plan is null)
            {
                return;
            }

            RecordStagePlan(plan);
            CommitControlDependenceStage(context, plan);
        }

        /// <summary>
        /// G0-P **R-3** 规划步：**只读**——算出批次，不触碰图。
        /// <para>
        /// 返回 <c>null</c> 表示该阶段无可做的事（依赖③的 <c>_dominanceOverlays</c> 为空），
        /// 与拆分前的提前 <c>return</c> 语义一致。
        /// </para>
        /// <para>
        /// ⚠ 时点刻意标为 <see cref="StagePlanTiming.DeferredUntilRuntimeInputs"/>：
        /// 本方法的**唯一**输入 <c>_dominanceOverlays</c> 由 Dominance 阶段在执行相位填充，
        /// 故该 plan 在规划相位快照中**必然缺席**——这是设计事实，不是缺陷。
        /// </para>
        /// </summary>
        private StagePlan? PlanControlDependenceStage(NLCPGBuildContext context)
        {
            if (DominanceOverlayCount == 0)
            {
                return null;
            }

            // 批次构造参数与原实现逐字一致（estimatedCost/NodeCount/Bytes 均为 1）。
            // ⚠ D1：**逐文件**构造批次，使 batch.StableOrder 恒为**该文件内**的覆盖层下标，
            //   且 batch.SourceFilePath 明确指向该文件——归并期据此路由到正确的图。
            //   单文件时（唯一的既有形态）产出与原实现**逐字相同**：同一个文件、
            //   下标 0..N-1、每批一项。
            var workBatches = new List<CpgWorkBatch>();
            foreach (var filePath in OverlayFilePaths)
            {
                var overlayCount = DominanceOverlaysFor(filePath).Count;
                for (var index = 0; index < overlayCount; index++)
                {
                    // ⚠ 必须用 9 参重载：8 参重载把 ShardOrder 默认成 stableOrder(=index)，
                    //   跨文件时各文件的 index 都从 0 起 ⇒ ShardOrder 重复 ⇒
                    //   执行器抛 "WorkBatch shard orders must be unique."。
                    //   stableOrder 保持文件内局部（供覆盖层下标），shardOrder 用全局计数。
                    workBatches.Add(new CpgWorkBatch(
                      workBatches.Count,
                      filePath,
                      index,
                      new[]
                      {
                          new CpgWorkItem(
                            index,
                            filePath,
                            methodSymbolKey: null,
                            spanStart: index,
                            spanEnd: index,
                            estimatedCost: 1,
                            kind: CpgWorkItemKind.Method),
                      },
                      estimatedCost: 1,
                      estimatedNodeCount: 1,
                      estimatedBytes: 1,
                      kind: CpgWorkBatchKind.Methods,
                      shardOrder: workBatches.Count));
                }
            }

            return new StagePlan(
              StageDependencyTable.Stage.ControlDependence,
              workBatches.ToArray(),
              StagePlanTiming.DeferredUntilRuntimeInputs,
              RequiresRuntimeInputFrom: StageDependencyTable.Stage.Dominance);
        }

        /// <summary>
        /// G0-P **R-3** 提交步：消费 plan，执行 worker 并归并（**唯一写图者**）。
        /// </summary>
        private void CommitControlDependenceStage(NLCPGBuildContext context, StagePlan plan)
        {
            var fragments = _workBatchExecutor.ExecuteAsync<LocalCpgFragment>(
              plan.Batches,
              (batch, _, _) => CollectControlDependenceFragment(
                context,
                // ⚠ **下标域修复（D1）**：原为 `_dominanceOverlays[batch.StableOrder]`，
                //   扁平列表 + 文件内局部序号 ⇒ 跨文件批次必然错配/越界。
                //   现按 batch 的**来源文件**取该文件自己的覆盖层列表，下标域回到单文件局部。
                ResolveDominanceOverlay(batch),
                batch),
              cancellationToken: CancellationToken.None,
              stageId: CpgWorkBatchPerformanceStageId.ControlDependence).GetAwaiter().GetResult();
            // G0-P R-2：回报本次实际产出（归并前），供统一记账。
            ReportStageFragments(fragments);
            // D1：按 fragment 的来源文件路由到各自的图。
            ReduceFragments(context, fragments);
        }

        /// <summary>
        /// 取批次所属文件的支配覆盖层（D1 下标域修复）。
        /// </summary>
        /// <remarks>
        /// 计划阶段按 <see cref="CpgWorkItem.SourceFilePath"/> 逐文件构造批次，
        /// 且该文件的批次下标恰为文件内局部 <c>StableOrder</c>，
        /// 故用 <see cref="CpgWorkBatch.SourceFilePath"/> 取列表即可。
        /// </remarks>
        private DominanceMethodOverlay ResolveDominanceOverlay(CpgWorkBatch batch)
        {
            var overlays = DominanceOverlaysFor(batch.SourceFilePath);
            var stableOrder = batch.StableOrder;
            if ((uint)stableOrder >= (uint)overlays.Count)
            {
                throw new InvalidOperationException(
                  $"ControlDependence 的批次与 {batch.SourceFilePath} 的支配覆盖层不自洽："
                  + $"StableOrder={stableOrder} 超出该文件覆盖层数 {overlays.Count}（G0-P R-3 / D1）。");
            }

            return overlays[stableOrder];
        }

        private static LocalCpgFragment CollectControlDependenceFragment(
          NLCPGBuildContext context,
          DominanceMethodOverlay overlay,
          CpgWorkBatch batch)
        {
            var localGraph = new NLCPGGraph(
              identityFactory: context.Graph.IdentityFactory,
              stringInterner: context.Graph.StringTable);
            var blockCount = overlay.ControlFlowGraph.Blocks.Max(block => block.Ordinal) + 1;
            var visitedEpochByBlockOrdinal = new int[blockCount];
            var visitedEpoch = 0;
            foreach (var block in overlay.ControlFlowGraph.Blocks)
            {
                if (overlay.ControlNodesByBlockOrdinal[block.Ordinal] is not { } controlNode)
                {
                    continue;
                }

                var immediatePostDominator = overlay.ImmediatePostDominatorByBlockOrdinal[block.Ordinal];
                var postDominators = overlay.PostDominatorsByBlockOrdinal[block.Ordinal];
                var fallThroughSuccessorOrdinal = block.FallThroughSuccessor?.Destination?.Ordinal;
                var conditionalSuccessorOrdinal = block.ConditionalSuccessor?.Destination?.Ordinal;
                if (fallThroughSuccessorOrdinal is { } fallThroughOrdinal &&
                    conditionalSuccessorOrdinal is { } conditionalOrdinal)
                {
                    if (fallThroughOrdinal <= conditionalOrdinal)
                    {
                        AddControlDependenceEdges(fallThroughOrdinal, controlNode, immediatePostDominator, postDominators, overlay, localGraph, visitedEpochByBlockOrdinal, ref visitedEpoch);
                        if (fallThroughOrdinal != conditionalOrdinal)
                        {
                            AddControlDependenceEdges(conditionalOrdinal, controlNode, immediatePostDominator, postDominators, overlay, localGraph, visitedEpochByBlockOrdinal, ref visitedEpoch);
                        }
                    }
                    else
                    {
                        AddControlDependenceEdges(conditionalOrdinal, controlNode, immediatePostDominator, postDominators, overlay, localGraph, visitedEpochByBlockOrdinal, ref visitedEpoch);
                        AddControlDependenceEdges(fallThroughOrdinal, controlNode, immediatePostDominator, postDominators, overlay, localGraph, visitedEpochByBlockOrdinal, ref visitedEpoch);
                    }
                }
                else if (fallThroughSuccessorOrdinal is { } singleFallThroughOrdinal)
                {
                    AddControlDependenceEdges(singleFallThroughOrdinal, controlNode, immediatePostDominator, postDominators, overlay, localGraph, visitedEpochByBlockOrdinal, ref visitedEpoch);
                }
                else if (conditionalSuccessorOrdinal is { } singleConditionalOrdinal)
                {
                    AddControlDependenceEdges(singleConditionalOrdinal, controlNode, immediatePostDominator, postDominators, overlay, localGraph, visitedEpochByBlockOrdinal, ref visitedEpoch);
                }
            }

            // scratch 取数：不再 FreezeQueryIndex（见 ControlFlowPass 同处说明）。
            // ⚠ 取数必须在任何冻结之前：冻结会 Release pending 缓冲。
            var descriptors = localGraph.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();
            var edges = localGraph.EnumerateScratchEdges()
              .Select(edge => new CpgEdgeCandidate(
                edge.SourceNode.StableAnchor!.Value,
                edge.TargetNode.StableAnchor!.Value,
                edge.Kind,
                edge.StructuredLabel,
                edge.ContextId,
                edge.CallSiteContext))
              .ToArray();
            // descriptors/edges 是本方法刚 ToArray 出来的独占数组，之后没有写入或复用，
            // 故交给 CreateOwned 接管，省掉构造器的第二次载荷复制。
            // ⚠ D1：fragment 的 SourceFilePath 决定归并时写入哪张图。
            //   必须是**批次条目所属文件**（batch.SourceFilePath），而不是构造期 context.FilePath——
            //   后者在跨文件批次下会把本文件的事实写进 A 文件的图。
            return LocalCpgFragment.CreateOwned(
              batch.BatchId,
              batch.SourceFilePath,
              batch.StableOrder,
              descriptors,
              edges,
              Array.Empty<CpgMethodSummary>(),
              Array.Empty<CpgBoundaryReference>(),
              new CpgFragmentMetrics(0, 0, descriptors.Length, edges.Length, descriptors.Length, edges.Length, descriptors.Length * 64 + edges.Length * 48),
              Array.Empty<CpgDiagnostic>());
        }

        private static void AddControlDependenceEdges(
            int successorOrdinal,
            NLCPGNode controlNode,
            int? immediatePostDominator,
          BlockBitSet postDominators,
          DominanceMethodOverlay overlay,
            NLCPGGraph graph,
            int[] visitedEpochByBlockOrdinal,
            ref int visitedEpoch)
        {
            // 后支配后继不受当前条件控制，直接跳过。
            if (postDominators.Contains(successorOrdinal))
            {
                return;
            }

            // 以 epoch 标记本次链遍历，避免每个后继都分配 HashSet。
            if (visitedEpoch == int.MaxValue)
            {
                Array.Clear(visitedEpochByBlockOrdinal);
                visitedEpoch = 0;
            }

            visitedEpoch += 1;
            var runner = successorOrdinal;
            while (runner != immediatePostDominator &&
                   visitedEpochByBlockOrdinal[runner] != visitedEpoch)
            {
                visitedEpochByBlockOrdinal[runner] = visitedEpoch;
                foreach (var dependentNode in overlay.NodesByBlockOrdinal[runner])
                {
                    // 控制节点自身不需要连回自己。
                    if (!dependentNode.Equals(controlNode))
                    {
                        graph.AddEdge(controlNode, dependentNode, NLCPGEdgeKind.ControlDependence);
                    }
                }

                // 走不到下一个后支配点时结束，避免悬空块死循环。
                var next = overlay.ImmediatePostDominatorByBlockOrdinal[runner];
                if (next is null)
                {
                    break;
                }

                runner = next.Value;
            }
        }
    }

}
