using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class ControlFlowPass : INLCPGPass
    {
        internal static ControlFlowPass Instance { get; } = new();

        private ControlFlowPass()
        {
        }

        public string Name => nameof(ControlFlowPass);

        // 触发控制流 pass，为方法边界和结构化分支补 CFG 边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunControlFlowPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        internal void RunControlFlowPass(NLCPGBuildContext context)
        {
            // G0-P R-3：本阶段的规划已在【执行相位之前】完成并登记（见 PlanStagesBeforeExecution）。
            // 这里只消费既有 plan。
            var plan = TakeRecordedStagePlan(StageDependencyTable.Stage.ControlFlow);
            if (plan is null)
            {
                // 规划相位【只在无操作根时】返回 null（见 PlanControlFlowStage）。
                // 若此处仍有操作根，说明规划相位没有覆盖本阶段 —— fail-closed，
                // 而不是静默跳过整阶段的 CFG（正是 G0-P 要消灭的失效形态）。
                // ⚠ D1：必须检查**全部文档**——只查驱动文档会在「首文件无方法、其余文件有」时
                //   把整体跳过误判为合法（静默少边）。
                if (HasAnyOperationRootAcrossDocuments(context))
                {
                    throw new InvalidOperationException(
                      "ControlFlow 阶段被请求执行，但规划相位没有登记它的 plan（G0-P R-3）。"
                      + "规划必须在任何 worker 启动之前完成；此处不重新规划，以免把规划时点拉回执行相位。");
                }

                return;
            }

            CommitControlFlowStage(context, plan);
        }

        /// <summary>
        /// G0-P **R-3** 规划步：**只读**——算出批次，不触碰图。
        /// <para>
        /// 由 <c>PlanStagesBeforeExecution</c> 在**执行相位之前**调用。
        /// 返回 <c>null</c> 表示无操作根（与拆分前的提前 <c>return</c> 语义一致）。
        /// </para>
        /// </summary>
        private StagePlan? PlanControlFlowStage(NLCPGBuildContext context)
        {
            // ⚠ D1：判据必须是**全部文档**是否有操作根。只查驱动文档时，
            //   「首文件恰好没有方法、其余文件有」会被误判为「无操作根」⇒ 整阶段返回 null
            //   ⇒ 规划相位不登记 plan ⇒ 其余文件的 CFG 边全部静默缺失。
            if (!HasAnyOperationRootAcrossDocuments(context))
            {
                return null;
            }

            return new StagePlan(
              StageDependencyTable.Stage.ControlFlow,
              // D1：跨文件聚合装箱（单文件时与 AssembleWorkBatches 逐字相同）。
              AssembleWorkBatchesAcrossDocuments(context));
        }

        /// <summary>
        /// G0-P **R-3** 提交步：消费 plan，执行 worker 并发布（**唯一写图者**）。
        /// </summary>
        private void CommitControlFlowStage(NLCPGBuildContext context, StagePlan plan)
        {
            // ⚠ D1：worker 按文件产出**多个** fragment（一个批次可跨文件——T3），
            //   故结果类型是 IReadOnlyList<LocalCpgFragment>，归并前先扁平化。
            //   单文件批次下恒只有一个元素，行为与改造前逐字一致。
            var perBatchFragments = _workBatchExecutor.ExecuteAsync<IReadOnlyList<LocalCpgFragment>>(
              plan.Batches,
              (batch, _, _) => CollectControlFlowFragments(context, batch),
              cancellationToken: CancellationToken.None,
              stageId: CpgWorkBatchPerformanceStageId.ControlFlow).GetAwaiter().GetResult();
            var fragments = perBatchFragments.SelectMany(batchFragments => batchFragments).ToArray();
            // G0-P R-2：回报本次实际产出（归并前），供统一记账。
            ReportStageFragments(fragments);
            PublishControlFlowFragments(context, fragments);
        }

        /// <summary>
        /// 为批次的**每个来源文件**各产出一个 fragment。
        /// </summary>
        /// <remarks>
        /// fragment 的 <c>SourceFilePath</c> 决定归并时写入哪张图，故必须用条目所属文件
        /// （而非构造期 <c>context.FilePath</c>）。
        /// </remarks>
        private IReadOnlyList<LocalCpgFragment> CollectControlFlowFragments(
          NLCPGBuildContext context,
          CpgWorkBatch batch)
        {
            var fragments = new List<LocalCpgFragment>();
            foreach (var group in SourceFilePartition.BySourceFile(batch.Items))
            {
                fragments.Add(CollectControlFlowFragment(context, batch, group));
            }

            return fragments;
        }

        private LocalCpgFragment CollectControlFlowFragment(
          NLCPGBuildContext context,
          CpgWorkBatch batch,
          SourceRoutedGroup<CpgWorkItem> group)
        {
            var localGraph = new NLCPGGraph(
              identityFactory: context.Graph.IdentityFactory,
              stringInterner: context.Graph.StringTable);
            // ⚠ D1：必须用**该分组所属文件**的 context 去解析操作根——
            //   AddMethodLevelControlFlow 会按 selectedRootOrder 在 context 自己的操作根里查找，
            //   用外层 context 会把 B 文件分组的序号解析成 A 文件的方法（静默错配）。
            var document = context.ResolveDocument(group.SourceFilePath);
            foreach (var item in group.Items.OrderBy(item => item.StableOrder))
            {
                AddMethodLevelControlFlow(document, localGraph, item.StableOrder);
            }

            // scratch 取数：不再 FreezeQueryIndex（那会为这条出口建一份随即变垃圾的完整查询索引：
            // 确定性 NodeId、CSR 邻接、按种类分桶、全图 SHA-256）。改为经具名 scratch 通道直接读
            // pending 边 —— 两端节点在 AddNode 时已带 StableAnchor，而片段的出口只要锚点。
            //
            // 为什么不能"只删掉 FreezeQueryIndex"：Edges 在未冻结时【静默返回空数组】
            // ⇒ 会无声产出空边集。故必须换数据源，而不是换调用时机。
            // ⚠ 取数必须在任何冻结之前：冻结会 Release pending 缓冲，此后读取抛异常。
            var nodeDescriptors = localGraph.Nodes
              .Select(CpgNodeDescriptor.FromNode)
              .ToArray();
            var edgeCandidates = localGraph.EnumerateScratchEdges()
              .Select(edge => new CpgEdgeCandidate(
                edge.SourceNode.StableAnchor!.Value,
                edge.TargetNode.StableAnchor!.Value,
                edge.Kind,
                edge.StructuredLabel,
                edge.ContextId,
                edge.CallSiteContext))
              .ToArray();
            // nodeDescriptors/edgeCandidates 是本方法刚 ToArray 出来的独占数组，
            // 之后没有写入、缓存或对象池归还，故交给 CreateOwned 接管，省掉构造器的第二次复制。
            return LocalCpgFragment.CreateOwned(
              batch.BatchId,
              group.SourceFilePath,
              // 单文件批次（既有唯一形态）下 group.Items 就是 batch.Items，
              // 故此处恒等于 batch.StableOrder，与改造前逐字一致。
              group.Items.Count == batch.Items.Count ? batch.StableOrder : group.Items[0].StableOrder,
              nodeDescriptors,
              edgeCandidates,
              Array.Empty<CpgMethodSummary>(),
              Array.Empty<CpgBoundaryReference>(),
              new CpgFragmentMetrics(
                0,
                0,
                nodeDescriptors.Length,
                edgeCandidates.Length,
                nodeDescriptors.Length,
                edgeCandidates.Length,
                checked(nodeDescriptors.Length * 64 + edgeCandidates.Length * 48)),
              Array.Empty<CpgDiagnostic>());
        }

        private void PublishControlFlowFragments(
          NLCPGBuildContext context,
          IReadOnlyList<LocalCpgFragment> fragments)
        {
            // ⚠ ControlFlow 是**唯一不走 reducer** 的发布点（手写 AddNode/AddControlFlowEdge）。
            //   故按项路由必须在这里**单独**落实，否则只改 reducer 会静默漏掉本阶段。
            //   按 fragment 的 SourceFilePath 分组：多文件下各写各的图。
            //   单文件时恒只有一组，且 resolve 回 context.Graph，行为逐字不变。
            foreach (var group in fragments
              .GroupBy(fragment => fragment.SourceFilePath, StringComparer.Ordinal)
              .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                PublishControlFlowFragmentsIntoGraph(context.ResolveGraph(group.Key), group);
            }
        }

        private void PublishControlFlowFragmentsIntoGraph(
          NLCPGGraph targetGraph,
          IEnumerable<LocalCpgFragment> fragments)
        {
            var descriptors = fragments
              .SelectMany(fragment => fragment.Nodes)
              .GroupBy(descriptor => descriptor.Anchor)
              .Select(group => group
                .OrderBy(descriptor => descriptor.Kind)
                .ThenBy(descriptor => descriptor.NameId)
                .First())
              .ToArray();
            var allocation = targetGraph.HasPreallocatedNodeIds
              ? targetGraph.RequirePreallocatedNodeIds()
              : DeterministicNodeIdTable.Create(descriptors.Select(descriptor => descriptor.Anchor));
            var nodesByAnchor = descriptors.ToDictionary(
              descriptor => descriptor.Anchor,
              descriptor => targetGraph.AddNode(descriptor.Materialize(allocation)));
            var seenEdges = new HashSet<CpgEdgeCandidate>();
            foreach (var candidate in fragments
              .SelectMany(fragment => fragment.Edges)
              .OrderBy(edge => edge.SourceAnchor.Kind)
              .ThenBy(edge => edge.SourceAnchor.FilePathId)
              .ThenBy(edge => edge.SourceAnchor.SpanStart)
              .ThenBy(edge => edge.SourceAnchor.SpanEnd)
              .ThenBy(edge => edge.TargetAnchor.Kind)
              .ThenBy(edge => edge.TargetAnchor.FilePathId)
              .ThenBy(edge => edge.TargetAnchor.SpanStart)
              .ThenBy(edge => edge.TargetAnchor.SpanEnd)
              .ThenBy(edge => edge.Kind))
            {
                if (!seenEdges.Add(candidate) ||
                    !nodesByAnchor.TryGetValue(candidate.SourceAnchor, out var source) ||
                    !nodesByAnchor.TryGetValue(candidate.TargetAnchor, out var target))
                {
                    continue;
                }

                AddControlFlowEdge(source, target, candidate.Kind, targetGraph);
            }
        }

        private void AddMethodLevelControlFlow(
          NLCPGBuildContext context,
          NLCPGGraph? targetGraph = null,
          int? selectedRootOrder = null)
        {
            var graph = targetGraph ?? context.Graph;
            foreach (var rootPlan in GetOperationRootPlans(context.Root, context.SemanticModel))
            {
                if (selectedRootOrder is not null && rootPlan.Order != selectedRootOrder.Value)
                {
                    continue;
                }

                if (GetOperationRoot(context, rootPlan.BodySyntax) is not IBlockOperation methodBlock ||
                    !IsMethodRootBlock(methodBlock))
                {
                    continue;
                }

                if (rootPlan.OwningMethod is IMethodSymbol methodSymbol)
                {
                    var entryNode = GetOrCreateMethodEntryNode(methodSymbol, graph);
                    var parameterNodes = methodSymbol.Parameters
                      .Select(parameter => GetOrCreateMethodParameterNode(methodSymbol, parameter, graph))
                      .ToList();
                    var returnNode = GetOrCreateMethodReturnNode(methodSymbol, graph);
                    var exitNode = GetOrCreateMethodExitNode(methodSymbol, graph);
                    var firstOperation = FirstExecutableOperation(methodBlock);

                    // 先串起 entry -> parameters -> 首个可执行节点；空方法则直接落到 return。
                    if (parameterNodes.Count > 0)
                    {
                        AddControlFlowEdge(entryNode, parameterNodes[0], NLCPGEdgeKind.CfgNext, graph);
                        for (var index = 0; index < parameterNodes.Count - 1; index += 1)
                        {
                            AddControlFlowEdge(parameterNodes[index], parameterNodes[index + 1], NLCPGEdgeKind.CfgNext, graph);
                        }

                        if (firstOperation is not null)
                        {
                            AddControlFlowEdge(
                              parameterNodes[^1],
                              GetOrCreateOperationNode(firstOperation, graph),
                              NLCPGEdgeKind.CfgNext,
                              graph);
                        }
                        else
                        {
                            AddControlFlowEdge(parameterNodes[^1], returnNode, NLCPGEdgeKind.CfgNext, graph);
                        }
                    }
                    else if (firstOperation is not null)
                    {
                        AddControlFlowEdge(entryNode, GetOrCreateOperationNode(firstOperation, graph), NLCPGEdgeKind.CfgNext, graph);
                    }
                    else
                    {
                        AddControlFlowEdge(entryNode, returnNode, NLCPGEdgeKind.CfgNext, graph);
                    }

                    // 显式 return 始终先汇入 MethodReturn，再由 MethodReturn 统一连向 MethodExit。
                    foreach (var returnOperation in methodBlock.DescendantsAndSelf().OfType<IReturnOperation>())
                    {
                        AddControlFlowEdge(GetOrCreateOperationNode(returnOperation, graph), returnNode, NLCPGEdgeKind.CfgNext, graph);
                    }

                    // 没有显式 return 且末尾仍可顺序流出时，补一条隐式 return 边。
                    var terminalOperation = methodBlock.Operations.LastOrDefault();
                    if (terminalOperation is not null && !ContainsExplicitReturn(methodBlock) && !StopsSequentialFlow(terminalOperation))
                    {
                        AddControlFlowEdge(GetOrCreateOperationNode(terminalOperation, graph), returnNode, NLCPGEdgeKind.CfgNext, graph);
                    }

                    AddControlFlowEdge(returnNode, exitNode, NLCPGEdgeKind.CfgNext, graph);
                }

                // 先铺普通顺序边，再补结构化控制流的额外语义。
                AddSequentialEdges(methodBlock.Operations, graph);
                foreach (var operation in methodBlock.Descendants())
                {
                    switch (operation)
                    {
                        case IConditionalOperation conditional:
                            AddConditionalEdges(conditional, graph);
                            break;
                        case IWhileLoopOperation whileLoop:
                            AddWhileLoopEdges(whileLoop, graph);
                            break;
                        case IForLoopOperation forLoop:
                            AddForLoopEdges(forLoop, graph);
                            break;
                        case ISwitchOperation switchOperation:
                            AddSwitchEdges(switchOperation, graph);
                            break;
                        case ITryOperation tryOperation:
                            AddTryEdges(tryOperation, graph);
                            break;
                        case IReturnOperation:
                            break;
                    }
                }

                AddLoopJumpEdges(methodBlock, graph);
            }
        }

        private void AddSequentialEdges(IEnumerable<IOperation> operations, NLCPGGraph graph)
        {
            var ordered = operations.ToList();
            for (var index = 0; index < ordered.Count - 1; index += 1)
            {
                if (StopsSequentialFlow(ordered[index]))
                {
                    continue;
                }

                AddControlFlowEdge(
                  GetOrCreateOperationNode(ordered[index], graph),
                  GetOrCreateOperationNode(ordered[index + 1], graph),
                  NLCPGEdgeKind.CfgNext,
                  graph);
            }

            foreach (var nestedBlock in ordered.OfType<IBlockOperation>())
            {
                AddSequentialEdges(nestedBlock.Operations, graph);
            }
        }

        private void AddConditionalEdges(IConditionalOperation conditional, NLCPGGraph graph)
        {
            var conditionNode = GetOrCreateOperationNode(conditional.Condition, graph);
            NLCPGNode? trueNode = conditional.WhenTrue is null ? null : GetOrCreateOperationNode(conditional.WhenTrue, graph);
            NLCPGNode? falseNode = conditional.WhenFalse is null ? null : GetOrCreateOperationNode(conditional.WhenFalse, graph);
            if (trueNode is not null)
            {
                AddControlFlowEdge(conditionNode, trueNode.Value, NLCPGEdgeKind.CfgTrue, graph);
            }

            if (falseNode is not null)
            {
                AddControlFlowEdge(conditionNode, falseNode.Value, NLCPGEdgeKind.CfgFalse, graph);
            }
        }

        private void AddWhileLoopEdges(IWhileLoopOperation whileLoop, NLCPGGraph graph)
        {
            if (whileLoop.Condition is null)
            {
                return;
            }

            var conditionNode = GetOrCreateOperationNode(whileLoop.Condition, graph);
            NLCPGNode? bodyNode = whileLoop.Body is null ? null : GetOrCreateOperationNode(whileLoop.Body, graph);
            if (bodyNode is null)
            {
                return;
            }

            AddControlFlowEdge(conditionNode, bodyNode.Value, NLCPGEdgeKind.CfgTrue, graph);
            AddControlFlowEdge(bodyNode.Value, conditionNode, NLCPGEdgeKind.CfgNext, graph);

            var exitTarget = NextSiblingOperation(whileLoop);
            if (exitTarget is not null)
            {
                AddControlFlowEdge(conditionNode, GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgFalse, graph);
            }
        }

        private void AddForLoopEdges(IForLoopOperation forLoop, NLCPGGraph graph)
        {
            var conditionOperation = forLoop.Condition ?? (forLoop.Before.Length > 0 ? forLoop.Before.LastOrDefault() : null);
            NLCPGNode? bodyNode = forLoop.Body is null ? null : GetOrCreateOperationNode(forLoop.Body, graph);
            if (conditionOperation is not null && bodyNode is not null)
            {
                var conditionNode = GetOrCreateOperationNode(conditionOperation, graph);
                AddControlFlowEdge(conditionNode, bodyNode.Value, NLCPGEdgeKind.CfgTrue, graph);
                AddControlFlowEdge(bodyNode.Value, conditionNode, NLCPGEdgeKind.CfgNext, graph);

                var exitTarget = NextSiblingOperation(forLoop);
                if (exitTarget is not null)
                {
                    AddControlFlowEdge(conditionNode, GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgFalse, graph);
                }
            }
        }

        private void AddSwitchEdges(ISwitchOperation switchOperation, NLCPGGraph graph)
        {
            var switchValueNode = GetOrCreateOperationNode(switchOperation.Value, graph);
            var exitTarget = NextSiblingOperation(switchOperation);
            var hasDefaultCase = false;
            var caseEntries = switchOperation.Cases
              .Select(@case => new
              {
                  Case = @case,
                  Entry = FirstExecutableOperation(@case.Body),
                  Terminal = LastExecutableOperation(@case.Body),
              })
              .ToList();

            // case 入口可能需要越过空 body，继续寻找后续可执行 case。
            foreach (var item in caseEntries)
            {
                var @case = item.Case;
                var caseBodyEntry = ResolveSwitchCaseEntry(caseEntries, @case, exitTarget);
                if (caseBodyEntry is null)
                {
                    continue;
                }

                AddControlFlowEdge(switchValueNode, GetOrCreateOperationNode(caseBodyEntry, graph), NLCPGEdgeKind.CfgTrue, graph);

                if (@case.Clauses.Any(clause => clause.CaseKind == CaseKind.Default))
                {
                    hasDefaultCase = true;
                }
            }

            if (!hasDefaultCase && exitTarget is not null)
            {
                AddControlFlowEdge(switchValueNode, GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgFalse, graph);
            }

            // case 尾部未终止时，沿 fallthrough 接到下一个可执行 case；break 则接到 switch 之后。
            for (var index = 0; index < caseEntries.Count; index += 1)
            {
                var @case = caseEntries[index].Case;
                var caseTerminal = caseEntries[index].Terminal;
                var nextCaseEntry = index + 1 < caseEntries.Count
                  ? ResolveSwitchCaseEntry(caseEntries, caseEntries[index + 1].Case, exitTarget)
                  : exitTarget;

                if (caseTerminal is not null &&
                    caseTerminal is not IBranchOperation { BranchKind: BranchKind.Break } &&
                    !StopsSequentialFlow(caseTerminal))
                {
                    if (nextCaseEntry is not null)
                    {
                        AddControlFlowEdge(
                          GetOrCreateOperationNode(caseTerminal, graph),
                          GetOrCreateOperationNode(nextCaseEntry, graph),
                          NLCPGEdgeKind.CfgNext,
                          graph);
                    }
                    else if (exitTarget is not null)
                    {
                        AddControlFlowEdge(
                          GetOrCreateOperationNode(caseTerminal, graph),
                          GetOrCreateOperationNode(exitTarget, graph),
                          NLCPGEdgeKind.CfgNext,
                          graph);
                    }
                }

                foreach (var operation in DescendantsAndSelf(@case.Body))
                {
                    if (operation is IBranchOperation { BranchKind: BranchKind.Break })
                    {
                        if (exitTarget is not null)
                        {
                            AddControlFlowEdge(
                              GetOrCreateOperationNode(operation, graph),
                              GetOrCreateOperationNode(exitTarget, graph),
                              NLCPGEdgeKind.CfgNext,
                              graph);
                        }
                    }
                }
            }
        }

        private static IOperation? ResolveSwitchCaseEntry(IEnumerable<dynamic> caseEntries, ISwitchCaseOperation @case, IOperation? exitTarget)
        {
            var entries = caseEntries.ToList();
            var startIndex = entries.FindIndex(item => ReferenceEquals(item.Case, @case));
            if (startIndex < 0)
            {
                return exitTarget;
            }

            for (var index = startIndex; index < entries.Count; index += 1)
            {
                if (entries[index].Entry is IOperation entry)
                {
                    return entry;
                }
            }

            return exitTarget;
        }

        private void AddTryEdges(ITryOperation tryOperation, NLCPGGraph graph)
        {
            var tryBodyEntry = FirstExecutableOperation(tryOperation.Body);
            var finallyEntry = tryOperation.Finally is null ? null : FirstExecutableOperation(tryOperation.Finally);
            var exitTarget = NextSiblingOperation(tryOperation);
            var tryTerminal = LastExecutableOperation(tryOperation.Body);

            // 优先进入 try body；空 try body 才直接进入 finally。
            if (tryBodyEntry is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(tryOperation, graph), GetOrCreateOperationNode(tryBodyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
            }
            else if (finallyEntry is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(tryOperation, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
            }

            // catch 入口由 try body 的异常分支触发；catch 结束后继续流向 finally 或 try 之后。
            foreach (var catchClause in tryOperation.Catches)
            {
                var catchEntry = FirstExecutableOperation(catchClause.Handler);
                var catchTerminal = LastExecutableOperation(catchClause.Handler);
                if (tryBodyEntry is not null && catchEntry is not null)
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(tryBodyEntry, graph), GetOrCreateOperationNode(catchEntry, graph), NLCPGEdgeKind.CfgFalse, graph);
                }

                if (catchEntry is not null && finallyEntry is not null)
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(catchEntry, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
                }

                if (catchTerminal is not null &&
                    !ReferenceEquals(catchTerminal, catchEntry) &&
                    !StopsSequentialFlow(catchTerminal))
                {
                    if (finallyEntry is not null)
                    {
                        AddControlFlowEdge(GetOrCreateOperationNode(catchTerminal, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
                    }
                    else if (exitTarget is not null)
                    {
                        AddControlFlowEdge(GetOrCreateOperationNode(catchTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
                    }
                }
            }

            // try 正常结束后也要继续流向 finally 或后继节点。
            if (tryTerminal is not null && !StopsSequentialFlow(tryTerminal))
            {
                if (finallyEntry is not null)
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(tryTerminal, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
                }
                else if (exitTarget is not null)
                {
                    AddControlFlowEdge(GetOrCreateOperationNode(tryTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
                }
            }

            var finallyTerminal = tryOperation.Finally is null ? null : LastExecutableOperation(tryOperation.Finally);
            if (finallyTerminal is not null && exitTarget is not null && !StopsSequentialFlow(finallyTerminal))
            {
                AddControlFlowEdge(GetOrCreateOperationNode(finallyTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
            }
            else if (finallyEntry is not null && exitTarget is not null)
            {
                AddControlFlowEdge(GetOrCreateOperationNode(finallyEntry, graph), GetOrCreateOperationNode(exitTarget, graph), NLCPGEdgeKind.CfgNext, graph);
            }

            // finally 存在时，return 先跳入 finally，由 finally 再决定后续出边。
            if (finallyEntry is not null)
            {
                foreach (var returnOperation in tryOperation.Descendants().OfType<IReturnOperation>())
                {
                    if (tryOperation.Finally is not null && IsWithinOperation(returnOperation, tryOperation.Finally))
                    {
                        continue;
                    }

                    AddControlFlowEdge(GetOrCreateOperationNode(returnOperation, graph), GetOrCreateOperationNode(finallyEntry, graph), NLCPGEdgeKind.CfgNext, graph);
                }
            }
        }

        private void AddLoopJumpEdges(IBlockOperation methodBlock, NLCPGGraph graph)
        {
            foreach (var loop in methodBlock.Descendants().OfType<ILoopOperation>())
            {
                var targets = LoopTargets(loop);
                if (targets.ContinueTarget is null && targets.BreakTarget is null)
                {
                    continue;
                }

                foreach (var operation in loop.Body?.DescendantsAndSelf() ?? Enumerable.Empty<IOperation>())
                {
                    if (operation.Kind == OperationKind.Branch)
                    {
                        var branch = (IBranchOperation)operation;
                        if (branch.BranchKind == BranchKind.Continue && targets.ContinueTarget is not null)
                        {
                            AddControlFlowEdge(
                              GetOrCreateOperationNode(operation, graph),
                              GetOrCreateOperationNode(targets.ContinueTarget, graph),
                              NLCPGEdgeKind.CfgNext,
                              graph);
                        }

                        if (branch.BranchKind == BranchKind.Break && targets.BreakTarget is not null)
                        {
                            AddControlFlowEdge(
                              GetOrCreateOperationNode(operation, graph),
                              GetOrCreateOperationNode(targets.BreakTarget, graph),
                              NLCPGEdgeKind.CfgNext,
                              graph);
                        }
                    }
                }
            }
        }

        private static LoopControlTargets LoopTargets(ILoopOperation loop)
        {
            return loop switch
            {
                IWhileLoopOperation whileLoop => new LoopControlTargets(whileLoop.Condition, NextSiblingOperation(whileLoop)),
                IForLoopOperation forLoop => new LoopControlTargets(
                  forLoop.Condition ?? (forLoop.Before.Length > 0 ? forLoop.Before.LastOrDefault() : null),
                  NextSiblingOperation(forLoop)),
                _ => new LoopControlTargets(null, NextSiblingOperation(loop)),
            };
        }

        private static IOperation? NextSiblingOperation(IOperation operation)
        {
            if (operation.Parent is not IBlockOperation parentBlock)
            {
                return null;
            }

            var siblings = parentBlock.Operations;
            for (var index = 0; index < siblings.Length - 1; index += 1)
            {
                if (ReferenceEquals(siblings[index], operation))
                {
                    return siblings[index + 1];
                }
            }

            return null;
        }

        private static IOperation? FirstExecutableOperation(IOperation operation)
        {
            if (operation is IBlockOperation blockOperation)
            {
                return blockOperation.Operations.FirstOrDefault();
            }

            return operation;
        }

        private static IOperation? FirstExecutableOperation(IEnumerable<IOperation> operations)
        {
            foreach (var operation in operations)
            {
                var executable = FirstExecutableOperation(operation);
                if (executable is not null)
                {
                    return executable;
                }
            }

            return null;
        }

        private static IOperation? LastExecutableOperation(IOperation operation)
        {
            return operation switch
            {
                IBlockOperation blockOperation => LastExecutableOperation(blockOperation.Operations),
                _ => operation,
            };
        }

        private static IOperation? LastExecutableOperation(IEnumerable<IOperation> operations)
        {
            foreach (var operation in operations.Reverse())
            {
                var executable = LastExecutableOperation(operation);
                if (executable is not null)
                {
                    return executable;
                }
            }

            return null;
        }

        private static IEnumerable<IOperation> DescendantsAndSelf(IEnumerable<IOperation> operations)
        {
            foreach (var operation in operations)
            {
                foreach (var descendant in operation.DescendantsAndSelf())
                {
                    yield return descendant;
                }
            }
        }

        private bool IsMethodRootBlock(IBlockOperation blockOperation)
        {
            return blockOperation.Parent is IMethodBodyOperation or IConstructorBodyOperation;
        }

        private static bool StopsSequentialFlow(IOperation operation)
        {
            return operation is IReturnOperation ||
                   operation is IBranchOperation { BranchKind: BranchKind.Break or BranchKind.Continue };
        }

        private static bool ContainsExplicitReturn(IBlockOperation blockOperation)
        {
            return blockOperation.Descendants().OfType<IReturnOperation>().Any();
        }

        private static bool IsWithinOperation(IOperation candidate, IOperation container)
        {
            for (var current = candidate.Parent; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, container))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
