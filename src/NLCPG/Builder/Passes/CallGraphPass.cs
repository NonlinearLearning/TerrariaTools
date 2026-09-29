using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Concurrency;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class CallGraphPass : INLCPGPass
    {
        internal static CallGraphPass Instance { get; } = new();

        private CallGraphPass()
        {
        }

        public string Name => nameof(CallGraphPass);

        // 触发调用图 pass，为调用点和调用目标边补齐图事实。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            builder.RunCallGraphPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        private sealed record CallGraphOperationWork(
            IInvocationOperation? Invocation,
            IPropertyReferenceOperation? PropertyReference)
        {
            internal SyntaxNode Syntax => (SyntaxNode?)Invocation?.Syntax ?? PropertyReference!.Syntax;
        }

        private sealed record CallGraphFact(
            int StableOrder,
            IInvocationOperation? Invocation,
            IPropertyReferenceOperation? PropertyReference,
            IReadOnlyList<IMethodSymbol> ResolvedCandidates);

        /// <summary>
        /// worker 结果。⚠ <b>按文件路由</b>：<see cref="Groups"/> 使归并回调能把事实
        /// 写入**它自己所属文件**的图，而非构造期的 <c>context.Graph</c>（D1）。
        /// 一个批次可跨多个文件（T3 已放宽装箱），故结果内部按文件分离；
        /// 但**仍是单个结果对象**，故执行器的归并/度量契约无需改动。
        /// </summary>
        private sealed record CallGraphWorkBatchResult(
            IReadOnlyList<SourceRoutedGroup<CallGraphFact>> Groups);

        internal void RunCallGraphPass(NLCPGBuildContext context)
        {
            // G0-P R-3：本阶段的规划已在【执行相位之前】完成并登记（见 PlanStagesBeforeExecution）。
            // 这里只消费既有 plan，并重建与 plan 一致的操作视图。
            var plan = TakeRecordedStagePlan(StageDependencyTable.Stage.CallGraph);
            if (plan is null)
            {
                // 规划相位【只在无可分析操作时】返回 null（见 PlanCallGraphStage）。
                if (BuildCallGraphOperationWork(context).Length != 0)
                {
                    throw new InvalidOperationException(
                      "CallGraph 阶段被请求执行，但规划相位没有登记它的 plan（G0-P R-3）。"
                      + "规划必须在任何 worker 启动之前完成；此处不重新规划，以免把规划时点拉回执行相位。");
                }

                _callGraphBatchCount = 0;
                return;
            }

            CommitCallGraphStage(context, plan);
        }

        /// <summary>
        /// G0-P **R-3** 规划步：**只读**——算出批次，不触碰图。
        /// <para>
        /// 返回 <c>null</c> 表示无可分析的调用/属性访问操作
        /// （与拆分前的提前 <c>return</c> 语义一致）。
        /// </para>
        /// <para>
        /// <b>为何可静态规划（源码确证）：</b>批次只由
        /// <c>context.InvocationOperations</c> 与 <c>context.PropertyReferenceOperations</c>
        /// 两个**快照集合**推导（排序 → 编号 → 构造 <c>CpgWorkItem</c> → <c>_workBatchBuilder.Build</c>），
        /// 再配合无实例状态的批次构造器；**不读图、不读前序 pass 的运行期产物**。
        /// 这两个集合由 Operation 阶段填充，而规划相位位于 Operation 阶段之后，故输入已就绪。
        /// </para>
        /// </summary>
        private StagePlan? PlanCallGraphStage(NLCPGBuildContext context)
        {
            var workItems = new List<CpgWorkItem>();
            var any = false;
            foreach (var document in context.Documents)
            {
                var operationWork = BuildCallGraphOperationWork(document);
                if (operationWork.Length == 0)
                {
                    continue;
                }

                any = true;
                workItems.AddRange(BuildCallGraphWorkItems(document, operationWork));
            }

            if (!any)
            {
                return null;
            }

            return new StagePlan(
              StageDependencyTable.Stage.CallGraph,
              // D1：跨文件聚合装箱。单文件时与 _workBatchBuilder.Build(context.FilePath, …) 逐字相同。
              // S5-2：走 BuildWorkBatches（装箱的**唯一入口**），由其套用全局分片序号。
              BuildWorkBatches(
                context.DocumentSet is null ? context.FilePath : workItems[0].SourceFilePath,
                workItems));
        }

        /// <summary>
        /// G0-P **R-3** 提交步：消费 plan，执行 worker 并发布（**唯一写图者**）。
        /// </summary>
        private void CommitCallGraphStage(NLCPGBuildContext context, StagePlan plan)
        {
            // ⚠ 必须在任何 worker 启动之前：把方法索引预建到**闭包**再冻结。
            //   否则 reducer 会在 worker 运行期间继续向索引注册（它注册的正是自己为
            //   别的操作选中的候选），使 worker 读到的 name:signature 桶内容取决于
            //   "读到的那一刻归并到哪一批"——这不是数据竞争（读写都在 _cacheGate 内），
            //   而是**读取时刻依赖**：两条路径都合法，答案却不同。
            FreezeCallGraphMethodIndex(context);

            // D1：**逐文件**建立 order→work 索引。StableOrder 是文件内局部序号，
            //   且批次可跨文件，故单一扁平表在跨文件下会取到别的文件的操作（静默错配）。
            var operationWorkByFile = new Dictionary<string, Dictionary<int, CallGraphOperationWork>>(
              StringComparer.Ordinal);
            foreach (var document in context.Documents)
            {
                operationWorkByFile[document.FilePath] = BuildCallGraphOperationWork(document)
                  .ToDictionary(item => item.StableOrder, item => item.Work);
            }

            var workBatches = plan.Batches;
            _callGraphBatchCount = workBatches.Count;
            _workBatchExecutor.ExecuteAsync(
              workBatches,
              (batch, _, cancellationToken) => CollectCallGraphWorkBatch(
                batch,
                operationWorkByFile,
                cancellationToken),
              result => PublishCallGraphWorkBatch(result, context),
              CancellationToken.None,
              CpgWorkBatchPerformanceStageId.CallGraph).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 在**任何 worker 启动之前**，把本阶段要用到的方法符号全部注册进查找索引，
        /// 使索引对候选扩展**闭合**、从而在本阶段内不再增长（即"冻结"）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>为什么必须冻结（读取时刻依赖，非数据竞争）：</b>
        /// 归并回调 <c>PublishCallGraphWorkBatch</c> → <c>AddCallSite</c> →
        /// <c>GetOrCreateSymbolNode(candidate)</c> → <c>RegisterMethodSymbol</c> 会在
        /// **worker 仍在运行期间**向 <c>_methodSymbolsByFullName</c> 追加条目，
        /// 而 worker 的 <c>ResolveExactMethodFallbackCandidates</c> 正在读同一个桶来选候选。
        /// 读写都在 <c>_cacheGate</c> 内，故**不会**字典损坏、**不会**抛异常——
        /// 但桶内容取决于"读到的那一刻已经归并了多少批"，于是同一个操作在不同时序下得到
        /// 合法的不同候选集。桶按**声明类型 + 名字 + 签名**分键后，
        /// 无关类型的同名方法（如所有 <c>get_Count:int()</c>）不再落进同一桶，
        /// 该"时刻"差异不再放大成可见的产物差异；但**冻结仍然必要**：
        /// 同一类型内后续批注册的实现候选若不预先注册，仍会因归并进度不同而时有时无。
        /// </para>
        /// <para>
        /// <b>为什么一遍就够（闭合论证）：</b>某操作的候选集 = <c>f(I) ∪ S</c>，其中
        /// <c>f(I)</c> 是从索引桶里取出的子集（必 ⊆ I，注册它们不产生新键），
        /// <c>S</c> 来自**源码**（<c>_declaredTypes</c>、接收者基类/接口链的成员），
        /// 与 I 无关。故把每个操作的 <c>S</c> 全部注册后，I 对候选扩展即闭合；
        /// 再迭代不会产生新条目。
        /// </para>
        /// <para>
        /// <b>为何不走 <c>ResolveEffectiveCallTargets</c>：</b>该入口在 <c>localCache</c> 为
        /// <c>null</c> 时读写共享的 <c>_resolvedCallTargetsByDispatchShape</c>。在索引尚不完整的
        /// 预热过程中写入会把**不完整**的解析结果缓存下来，之后被归并回调复用——
        /// 既有的未命中路径反而被污染。故这里直接调用候选层
        /// （<c>ResolveCallTargetCandidates</c> / <c>ResolveAccessorTargetCandidates</c>），
        /// 它们只读索引、只写批次局部状态，不触碰任何共享缓存。
        /// </para>
        /// </remarks>
        private void FreezeCallGraphMethodIndex(NLCPGBuildContext context)
        {
            var frozenCandidateCount = 0;
            // 遍历顺序 = 登记序 + 文件内按 span 排序（BuildCallGraphOperationWork 保证），
            // 故注册序确定；同名键的追加顺序因此与并发时序无关。
            foreach (var document in context.Documents)
            {
                foreach (var (work, _) in BuildCallGraphOperationWork(document))
                {
                    if (work.Invocation is { } invocation)
                    {
                        if (invocation.TargetMethod is not { } targetMethod)
                        {
                            continue;
                        }

                        RegisterMethodSymbol(targetMethod);
                        frozenCandidateCount += 1;
                        foreach (var candidate in ResolveCallTargetCandidates(invocation, targetMethod))
                        {
                            RegisterMethodSymbol(candidate);
                            frozenCandidateCount += 1;
                        }

                        continue;
                    }

                    var propertyReference = work.PropertyReference!;
                    var accessorMethod = ResolvePropertyAccessorMethod(propertyReference);
                    if (accessorMethod is null)
                    {
                        continue;
                    }

                    RegisterMethodSymbol(accessorMethod);
                    frozenCandidateCount += 1;
                    foreach (var candidate in
                      ResolveAccessorTargetCandidates(accessorMethod, propertyReference.Instance?.Type))
                    {
                        RegisterMethodSymbol(candidate);
                        frozenCandidateCount += 1;
                    }
                }
            }

            // 留证：本阶段确实执行过冻结，且规模非零。索引冻结与否在产物上**等价**
            // （都产出同一批候选），故无法用行为断言区分"冻结了"与"没冻结"——
            // 与 _dataFlowPlanAssemblyCount 同理，需要这个计数供契约测试判定。
            _callGraphFrozenMethodIndexCount = frozenCandidateCount;
        }

        /// <summary>
        /// 构造本阶段的操作工作视图。
        /// <para>
        /// ⚠ 规划与提交**必须**用同一套推导：本方法把"排序 + 稳定编号"固定在一处，
        /// 使 <c>StableOrder</c> 在两个相位之间一一对应。若各写一份，
        /// 提交步就会按错误的序号取操作——这是静默错配而非编译错误。
        /// </para>
        /// </summary>
        private static (CallGraphOperationWork Work, int StableOrder)[] BuildCallGraphOperationWork(
          NLCPGBuildContext context)
        {
            return context.InvocationOperations
              .Select(invocation => new CallGraphOperationWork(invocation, null))
              .Concat(context.PropertyReferenceOperations.Select(property => new CallGraphOperationWork(null, property)))
              .OrderBy(work => work.Syntax.SpanStart)
              .ThenBy(work => work.Syntax.Span.End)
              .ThenBy(work => work.Invocation is null ? 1 : 0)
              .Select((work, stableOrder) => (Work: work, StableOrder: stableOrder))
              .ToArray();
        }

        private static CpgWorkItem[] BuildCallGraphWorkItems(
          NLCPGBuildContext context,
          (CallGraphOperationWork Work, int StableOrder)[] operationWork)
        {
            return operationWork
              .Select(item => new CpgWorkItem(
                item.StableOrder,
                context.FilePath,
                null,
                item.Work.Syntax.SpanStart,
                item.Work.Syntax.Span.End,
                1,
                CpgWorkItemKind.Declaration))
              .ToArray();
        }

        private CallGraphWorkBatchResult CollectCallGraphWorkBatch(
          CpgWorkBatch batch,
          IReadOnlyDictionary<string, Dictionary<int, CallGraphOperationWork>> operationWorkByFile,
          CancellationToken cancellationToken)
        {
            // ⚠ 一个批次可跨多个文件（T3），故结果内按文件分离，各自可路由到正确的图。
            //   单文件批次下恒只有一组，行为与改造前逐字一致。
            var groups = new List<SourceRoutedGroup<CallGraphFact>>();
            foreach (var group in SourceFilePartition.BySourceFile(batch.Items))
            {
                var dispatchCache = new Dictionary<string, IReadOnlyList<IMethodSymbol>>(StringComparer.Ordinal);
                // ⚠ 用**该文件自己的** order→work 索引（D1）。
                var operationWorkByOrder = operationWorkByFile.TryGetValue(group.SourceFilePath, out var fileWork)
                  ? fileWork
                  : new Dictionary<int, CallGraphOperationWork>();
                var facts = new List<CallGraphFact>(group.Items.Count);
                foreach (var item in group.Items.OrderBy(item => item.StableOrder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!operationWorkByOrder.TryGetValue(item.StableOrder, out var work))
                    {
                        throw new InvalidOperationException(
                          $"Call graph WorkBatch item {item.StableOrder} has no operation fact.");
                    }

                    if (work.Invocation is { } invocation)
                    {
                        var targetMethod = invocation.TargetMethod;
                        var resolvedCandidates = targetMethod is null
                          ? Array.Empty<IMethodSymbol>()
                          : ResolveEffectiveCallTargets(invocation, targetMethod, dispatchCache);
                        facts.Add(new CallGraphFact(
                          item.StableOrder,
                          invocation,
                          null,
                          resolvedCandidates));
                        continue;
                    }

                    var propertyReference = work.PropertyReference!;
                    var accessorMethod = ResolvePropertyAccessorMethod(propertyReference);
                    var accessorCandidates = accessorMethod is null
                      ? Array.Empty<IMethodSymbol>()
                      : ResolvePreferredCallTargets(
                        ResolveAccessorTargetCandidates(accessorMethod, propertyReference.Instance?.Type),
                        accessorMethod,
                        propertyReference.Instance?.Type).ToArray();
                    facts.Add(new CallGraphFact(
                      item.StableOrder,
                      null,
                      propertyReference,
                      accessorCandidates));
                }

                groups.Add(new SourceRoutedGroup<CallGraphFact>(group.SourceFilePath, facts));
            }

            return new CallGraphWorkBatchResult(groups);
        }

        /// <summary>
        /// 把事实发布到**各自所属文件**的图（D1 按项路由）。
        /// </summary>
        /// <remarks>
        /// 旧实现直接收 <c>context.Graph</c>；多文件下那会把 B 文件的事实写进 A 文件的图。
        /// </remarks>
        private void PublishCallGraphWorkBatch(CallGraphWorkBatchResult result, NLCPGBuildContext context)
        {
            foreach (var group in result.Groups)
            {
                var graph = context.ResolveGraph(group.SourceFilePath);
                foreach (var fact in group.Items.OrderBy(fact => fact.StableOrder))
                {
                    if (fact.Invocation is { } invocation)
                    {
                        var operationNode = GetOrCreateOperationNode(invocation, graph);
                        AddCallSite(invocation, operationNode, graph, fact.ResolvedCandidates);
                        continue;
                    }

                    var propertyReference = fact.PropertyReference!;
                    var operationNodeForProperty = GetOrCreateOperationNode(propertyReference, graph);
                    AddPropertyAccessorCallSite(
                      propertyReference,
                      operationNodeForProperty,
                      graph,
                      fact.ResolvedCandidates);
                }
            }
        }

        private void AddCallSite(
          IInvocationOperation invocationOperation,
          NLCPGNode operationNode,
          NLCPGGraph graph,
          IReadOnlyList<IMethodSymbol>? precomputedCandidates = null)
        {
            var targetMethod = invocationOperation.TargetMethod;
            // Roslyn 已解析到目标时，继续扩充候选集并按内部优先规则排序。
            var resolvedCandidates = targetMethod is null
              ? null
              : precomputedCandidates ?? ResolveEffectiveCallTargets(invocationOperation, targetMethod);
            // 调用点节点复用操作节点的源码位置，但名字和签名以目标方法为准。
            var callSiteNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: NLCPGNodeKind.CallSite,
              Name: targetMethod?.Name ?? graph.ResolveName(operationNode),
              FullName: targetMethod is null ? graph.ResolveFullName(operationNode) : ComposeInvocationMethodFullName(targetMethod),
              Signature: targetMethod is null ? graph.ResolveSignature(operationNode) : ComposeInvocationSignature(targetMethod),
              DispatchKind: targetMethod is null
                ? null
                : ComposeResolvedDispatchKind(
                  resolvedCandidates![0],
                  targetMethod,
                  invocationOperation.Instance?.Type,
                  ComposeCallDispatchKind(resolvedCandidates[0], invocationOperation.Instance is not null)),
              TypeFullName: ComposeTypeFullName(invocationOperation.Type),
              FilePath: graph.ResolveFilePath(operationNode),
              SpanStart: operationNode.SpanStart,
              SpanEnd: operationNode.SpanEnd));
            graph.AddEdge(operationNode, callSiteNode, NLCPGEdgeKind.SyntaxChild);
            // G0-P R.3 L1：本方法是**归并回调**，运行在归并线程上；执行器已为归并线程
            // 开共享态窗口，故这些 builder 级具名容器的写入走 WriteSharedState。
            //
            // ⚠ 如实记录：按**当前**代码，这里并非活跃的数据竞争。它成立依赖两条
            //   「恰好如此、却从未写下」的事实：① 本阶段归并回调只有一个
            //   （resultChannel 为 SingleReader），彼此串行；② 这些容器的读者
            //   （DataFlowPass 的 FindCallSiteNode）都在本阶段结束后的**串行**相位。
            //   本机制不改变今天的可观测行为，它消除的是"把 L1 不变式寄托在这两条巧合上"
            //   ——任一条将来变化（多归并者，或让 worker 也读写），就会变成静默损坏
            //   （丢失更新/枚举中途变形，**不抛异常**，与轮次 29 实测同源）。
            WriteSharedState(
              () => _callSiteNodesByInvocation[invocationOperation] = callSiteNode);

            // 只有拿到目标方法时，才继续补充调用目标和求值类型。
            if (targetMethod is not null)
            {
                WriteSharedState(
                  () => _resolvedCallTargetsByInvocation[invocationOperation] = resolvedCandidates!);

                foreach (var candidateMethod in resolvedCandidates!)
                {
                    var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
                    graph.AddEdge(callSiteNode, methodNode, NLCPGEdgeKind.CallTargets);
                }

                AddEvalTypeEdge(callSiteNode, targetMethod.ReturnType, graph);
            }
        }

        private NLCPGNode? AddPropertyAccessorCallSite(
          IPropertyReferenceOperation propertyReference,
          NLCPGNode operationNode,
          NLCPGGraph graph,
          IReadOnlyList<IMethodSymbol>? precomputedCandidates = null)
        {
            var accessorMethod = ResolvePropertyAccessorMethod(propertyReference);
            // 无访问器可解析时直接退出，避免写出半截调用点。
            if (accessorMethod is null)
            {
                return null;
            }

            // 访问器候选解析与普通方法类似，但输入是属性访问器符号。
            var resolvedCandidates = precomputedCandidates ?? ResolvePreferredCallTargets(
                ResolveAccessorTargetCandidates(accessorMethod, propertyReference.Instance?.Type),
                accessorMethod,
                propertyReference.Instance?.Type);
            var accessorEvalType = ResolvePropertyAccessorEvalType(propertyReference, accessorMethod);
            // 访问器调用点同样挂在原操作节点下，方便后续统一查询。
            var callSiteNode = graph.AddNode(new NLCPGNodeDraft(
              Kind: NLCPGNodeKind.CallSite,
              Name: accessorMethod.Name,
              FullName: ComposeInvocationMethodFullName(accessorMethod),
              Signature: ComposeInvocationSignature(accessorMethod),
              DispatchKind: ComposeResolvedDispatchKind(
                resolvedCandidates[0],
                accessorMethod,
                propertyReference.Instance?.Type,
                ComposePropertyAccessorDispatchKind(resolvedCandidates[0], propertyReference.Instance is not null)),
              TypeFullName: ComposeTypeFullName(accessorEvalType),
              FilePath: graph.ResolveFilePath(operationNode),
              SpanStart: operationNode.SpanStart,
              SpanEnd: operationNode.SpanEnd));
            graph.AddEdge(operationNode, callSiteNode, NLCPGEdgeKind.SyntaxChild);
            // G0-P R.3 L1：同 AddCallSite——归并线程上的共享态写入必须经 WriteSharedState。
            WriteSharedState(
              () => _propertyAccessorCallSiteNodesByKey[PropertyAccessorCallSiteKey(propertyReference, accessorMethod)] =
                callSiteNode);

            foreach (var candidateMethod in resolvedCandidates)
            {
                var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
                graph.AddEdge(callSiteNode, methodNode, NLCPGEdgeKind.CallTargets);
            }

            AddEvalTypeEdge(callSiteNode, accessorEvalType, graph);
            return callSiteNode;
        }

        private static ITypeSymbol? ResolvePropertyAccessorEvalType(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod)
        {
            if (accessorMethod.ReturnType.SpecialType != SpecialType.System_Void)
            {
                return accessorMethod.ReturnType;
            }

            return propertyReference.Type ?? propertyReference.Property.Type;
        }

        private IEnumerable<IMethodSymbol> ResolveCallTargetCandidates(IInvocationOperation invocationOperation, IMethodSymbol targetMethod)
        {
            targetMethod = CanonicalMethodSymbol(targetMethod);
            // 先放入 Roslyn 当前解析结果，后续候选都在这个集合上去重补充。
            var candidates = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal)
            {
                [SymbolId(targetMethod)] = targetMethod,
            };

            // 接收者类型同时是候选**准入**条件（见 ResolveExactMethodFallbackCandidates），
            // 故必须早于精确回退的补齐计算。
            var receiverType = invocationOperation.Instance?.Type;

            // 再补同 full name 或同 name+signature 命中的精确回退候选。
            foreach (var exactMethod in ResolveExactMethodFallbackCandidates(targetMethod, receiverType))
            {
                candidates[SymbolId(exactMethod)] = exactMethod;
            }

            // 无接收者时无法继续做动态分派扩展，返回已收集的静态候选。
            if (receiverType is null)
            {
                return candidates.Values;
            }

            var targetDeclaringType = targetMethod.ContainingType;
            // 目标类型缺失时只保留静态候选，避免做不可靠的继承链猜测。
            if (targetDeclaringType is null)
            {
                return candidates.Values;
            }

            var baseDefinition = targetMethod.OriginalDefinition.OverriddenMethod ?? targetMethod.OriginalDefinition;

            // 先在当前工程已声明类型里找最可能的内部动态分派目标。
            foreach (var declaredType in _declaredTypes)
            {
                if (!InheritsFrom(declaredType, targetDeclaringType) ||
                    !InheritsFrom(declaredType, receiverType))
                {
                    continue;
                }

                foreach (var member in declaredType.GetMembers(targetMethod.Name).OfType<IMethodSymbol>())
                {
                    var canonicalMember = CanonicalMethodSymbol(member);
                    if (!MethodSignatureMatches(member, targetMethod) ||
                        !CanDispatchToCandidate(canonicalMember, targetMethod, baseDefinition, declaredType, receiverType))
                    {
                        continue;
                    }

                    candidates[SymbolId(canonicalMember)] = canonicalMember;
                }
            }

            // 再沿接收者基类/接口链补齐可落到外部或上层类型的候选。
            foreach (var superType in EnumerateBaseTypes(receiverType))
            {
                foreach (var member in superType.GetMembers(targetMethod.Name).OfType<IMethodSymbol>())
                {
                    var canonicalMember = CanonicalMethodSymbol(member);
                    if (!MethodSignatureMatches(member, targetMethod) ||
                        !CanDispatchToCandidate(canonicalMember, targetMethod, baseDefinition, superType, receiverType))
                    {
                        continue;
                    }

                    candidates[SymbolId(canonicalMember)] = canonicalMember;
                }
            }

            // 补上同签名的父类回退候选，覆盖 Roslyn 未直接给出的上层实现。
            foreach (var superMethod in ResolveSuperTypeFallbackCandidates(targetMethod, receiverType))
            {
                candidates[SymbolId(superMethod)] = superMethod;
            }

            // 扩展方法需要单独从方法索引里按接收者类型回收候选。
            foreach (var extensionMethod in ResolveReceiverAwareExtensionCandidates(targetMethod, receiverType))
            {
                candidates[SymbolId(extensionMethod)] = extensionMethod;
            }

            return candidates.Values;
        }

        private IReadOnlyList<IMethodSymbol> ResolveEffectiveCallTargets(
          IInvocationOperation invocationOperation,
          IMethodSymbol targetMethod,
          IDictionary<string, IReadOnlyList<IMethodSymbol>>? localCache = null)
        {
            var canonicalTarget = CanonicalMethodSymbol(targetMethod);
            var receiverType = invocationOperation.Instance?.Type;
            var cacheKey = $"{ComposeMethodFullName(canonicalTarget)}|{ComposeTypeFullName(receiverType)}";
            var cache = localCache ?? _resolvedCallTargetsByDispatchShape;
            if (cache.TryGetValue(cacheKey, out var cachedTargets))
            {
                return cachedTargets;
            }

            var resolvedTargets = ResolvePreferredCallTargets(
              ResolveCallTargetCandidates(invocationOperation, canonicalTarget),
              canonicalTarget,
              receiverType)
              .ToArray();

            // G0-P R.3 L1：worker 路径总是传入**批次局部** dispatchCache（见
            // CollectCallGraphWorkBatch），故 worker 从不写本字段；只有归并回调
            // （AddCallSite，此时 localCache 为 null）会写它，而归并线程处于共享态窗口内，
            // 因此同样经 WriteSharedState。
            //
            // 这里刻意**不**依赖"恰好只有归并者写"这一未写下的巧合：那是"声明代替机制"，
            // 且一旦某天 worker 漏传 localCache，就会退化成无门的共享字典写入（静默损坏）。
            // 分支把两种情形分开，使 worker 路径保持纯批次局部、零加锁。
            if (localCache is null)
            {
                WriteSharedState(
                  () => _resolvedCallTargetsByDispatchShape[cacheKey] = resolvedTargets);
            }
            else
            {
                localCache[cacheKey] = resolvedTargets;
            }

            return resolvedTargets;
        }

        /// <summary>
        /// 从方法索引里回收与 <paramref name="targetMethod"/> 精确相关的候选。
        /// </summary>
        /// <param name="receiverType">
        /// 接收者类型。索引现按**声明类型 + 名字 + 签名**分键（与 <c>ComposeMethodFullName</c> 同键），
        /// 故桶内已是"同一类型内同名同签名"的方法；本参数仍用于把与接收者无亲缘关系的候选挡在外面。
        /// 为 <c>null</c> 时不再额外过滤。
        /// </param>
        /// <remarks>
        /// <b>声明类型已并入查找键（原"方案 2"，现已采纳）：</b>
        /// 旧形态的键只含名字+签名，于是整个编译期所有 <c>get_Count:int()</c> 落进同一桶，
        /// 无关内部类型的方法会被 <c>PreferCallTargets</c> 的 internal 分支选中（该分支不校验全名，
        /// 且 internal 无条件 +1000），把错误候选经 <c>resolvedCandidates[0]</c> 写进 <c>DispatchKind</c>。
        /// 并入声明类型后，桶内只剩同类型方法，该误选路径消失。
        /// </remarks>
        /// <remarks>
        /// <b>历史代价（已消解）：</b>并入声明类型后旧键与 <c>ComposeMethodFullName</c> **同键**，
        /// 曾使 <c>_methodSymbolsByNameAndSignature</c> 与 <c>_methodSymbolsByFullName</c>
        /// 退化为同一张表。冗余表**已删除**，本方法现只查后者一张表。
        /// 跨类型回收接口/基类实现候选的能力**仍有**，但不再来自本桶，
        /// 而来自 <c>_declaredTypes</c> 扫描、接收者基类/接口链枚举、
        /// 以及 <c>ResolveSuperTypeFallbackCandidates</c>——它们都按类型显式枚举。
        /// </remarks>
        private IEnumerable<IMethodSymbol> ResolveExactMethodFallbackCandidates(
          IMethodSymbol targetMethod,
          ITypeSymbol? receiverType = null)
        {
            var fullName = ComposeMethodFullName(targetMethod);
            var candidates = new List<IMethodSymbol>();
            List<IMethodSymbol>? bucketCandidates = null;
            lock (_cacheGate)
            {
                // ⚠ 原先此处读两张表：
                //   ① _methodSymbolsByFullName[fullName]      —— 命中**无条件**入候选
                //   ② _methodSymbolsByNameAndSignature[旧键]  —— 命中须过 IsRelatedToTarget
                //   声明类型并入查找键后 ② 与 ① **同键同内容**（见 RegisterMethodSymbol），
                //   故 ② 作为冗余表已删除；这里两段准入逻辑原样保留、只是同源读 ①。
                //   刻意不把两段合并成单一路径：那会在"删冗余表"的同时改变候选集。
                //
                //   在门内取快照：桶是 List，出锁后枚举会与 reducer 的 Add 竞态（枚举中途变形）。
                if (_methodSymbolsByFullName.TryGetValue(fullName, out var sameBucket))
                {
                    candidates.AddRange(sameBucket);
                    bucketCandidates = new List<IMethodSymbol>(sameBucket);
                }
            }

            if (bucketCandidates is not null)
            {
                foreach (var candidate in bucketCandidates)
                {
                    if (IsRelatedToTarget(candidate, targetMethod, receiverType))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// 判定候选是否与目标处在同一分派族——用于过滤 <c>name:signature</c> 桶里的无关同名方法。
        /// </summary>
        /// <remarks>
        /// <b>为何这条过滤是必要的（正确性，非洁癖）：</b>
        /// <c>PreferCallTargets</c> 的 internal 分支（<c>internalMethods</c>）**只**看
        /// <c>IsInternalMethod</c>，不校验全名，且 internal 无条件压过 external
        /// （<c>CallTargetScore</c> 给 internal +1000）。于是当目标指向元数据里的
        /// <c>IReadOnlyCollection&lt;T&gt;.get_Count</c>（internal=false）时，任何**无关**内部类型的
        /// <c>get_Count:int()</c> 一旦进了候选集，就会被选中为分派目标，并经
        /// <c>resolvedCandidates[0]</c> 写进 <c>dispatchKind</c> 而改变节点 id。
        /// 这既是此前实测「同 span 同 fullName 而 dispatchKind 在 internal/external 间翻转」的
        /// 直接成因，也是一个真实的错误候选问题（不只是不确定性）。
        /// </remarks>
        private static bool IsRelatedToTarget(
          IMethodSymbol candidate,
          IMethodSymbol targetMethod,
          ITypeSymbol? receiverType)
        {
            // 原始定义一致 ⇒ 同一 override/泛型族，即使在元数据里也认可。
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, targetMethod.OriginalDefinition))
            {
                return true;
            }

            // override 链命中，或候选重写了目标（含显式接口实现）。
            if (candidate.OverriddenMethod is not null &&
                SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, targetMethod.OriginalDefinition))
            {
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, targetMethod.OriginalDefinition.OverriddenMethod))
            {
                return true;
            }

            // 显式接口实现：候选实现的就是目标那个接口方法。
            foreach (var implemented in candidate.ExplicitInterfaceImplementations)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, targetMethod.OriginalDefinition))
                {
                    return true;
                }
            }

            foreach (var implemented in targetMethod.ExplicitInterfaceImplementations)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, candidate.OriginalDefinition))
                {
                    return true;
                }
            }

            var targetDeclaringType = targetMethod.ContainingType;
            var candidateDeclaringType = candidate.ContainingType;
            if (targetDeclaringType is null || candidateDeclaringType is null)
            {
                // 无声明类型无法判定亲缘：保守放行，避免把既有可解析的候选误滤掉。
                return true;
            }

            // 声明类型之间存在继承/实现关系（双向），才算同一分派族。
            // ⚠ 双向都要放行：目标声明类型是候选的基类/接口（候选是内部实现），
            //   或候选声明类型是目标的基类（目标是内部实现，候选是外部回退）。
            if (AreTypesRelated(candidateDeclaringType, targetDeclaringType))
            {
                return true;
            }

            // 菱形接口：实际接收者**同时**实现两侧声明类型时，两者都是合法分派目标。
            // 这是唯一保留"声明类型彼此无关"的情形，故必须显式写出，否则会误滤。
            if (receiverType is INamedTypeSymbol receiver &&
                AreTypesRelated(receiver, targetDeclaringType) &&
                AreTypesRelated(receiver, candidateDeclaringType))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 两个类型是否同族：相等、或存在任一方向的继承/实现关系。
        /// </summary>
        /// <remarks>
        /// <b>为什么要同时比较构造类型与原始定义：</b>
        /// <c>SymbolEqualityComparer.Default</c> 区分类型实参，故 <c>IFoo&lt;int&gt;</c> 与
        /// <c>IFoo&lt;T&gt;</c> **不相等**，<c>AllInterfaces</c> 给出的也是构造后的接口。
        /// 泛型上下文里目标的声明类型常是未构造定义，只比构造类型会产生**假阴性**
        /// ——把真实候选误滤掉。故这里两个方向各比一遍构造类型与原定义；
        /// 判据向"纳入"倾斜是刻意的：本方法的职责是排除**明显无关**的类型
        /// （如无关内部类型的同名静态属性），而不是做精确分派推导。
        /// </remarks>
        private static bool AreTypesRelated(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            if (SymbolEqualityComparer.Default.Equals(left, right) ||
                SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition))
            {
                return true;
            }

            return InheritsFrom(left, right) ||
              InheritsFrom(right, left) ||
              InheritsFrom(left.OriginalDefinition, right.OriginalDefinition) ||
              InheritsFrom(right.OriginalDefinition, left.OriginalDefinition);
        }

        private IEnumerable<IMethodSymbol> ResolveSuperTypeFallbackCandidates(IMethodSymbol targetMethod, ITypeSymbol receiverType)
        {
            foreach (var superType in EnumerateBaseTypes(receiverType))
            {
                foreach (var member in superType.GetMembers(targetMethod.Name).OfType<IMethodSymbol>())
                {
                    var canonicalMember = CanonicalMethodSymbol(member);
                    if (!MethodSignatureMatches(canonicalMember, targetMethod))
                    {
                        continue;
                    }

                    yield return canonicalMember;
                }
            }
        }

        private IEnumerable<IMethodSymbol> ResolveReceiverAwareExtensionCandidates(IMethodSymbol targetMethod, ITypeSymbol receiverType)
        {
            if (!targetMethod.IsExtensionMethod && targetMethod.ReducedFrom is null)
            {
                yield break;
            }

            // 归并 reducer 会在 worker 运行期间向该字典写入新方法，故先持门快照再枚举。
            //
            // 原先扫描的是已删除的 _methodSymbolsByNameAndSignature。该表与
            // _methodSymbolsByFullName **同键同内容**，故换成后者**不改变**扫到的集合：
            // 两者都登记同一批 canonicalMethod（见 RegisterMethodSymbol）。
            List<IMethodSymbol> extensionCandidates;
            lock (_cacheGate)
            {
                extensionCandidates = _methodSymbolsByFullName.Values
                  .SelectMany(methodGroup => methodGroup)
                  .Where(method => method.IsExtensionMethod)
                  .ToList();
            }

            foreach (var method in extensionCandidates)
            {
                var canonicalMethod = CanonicalMethodSymbol(method);
                if (!MethodSignatureMatches(canonicalMethod, targetMethod) ||
                    !CanDispatchToExtensionReceiver(canonicalMethod, receiverType))
                {
                    continue;
                }

                yield return canonicalMethod;
            }
        }

        private IEnumerable<IMethodSymbol> ResolveAccessorTargetCandidates(IMethodSymbol accessorMethod, ITypeSymbol? receiverType)
        {
            accessorMethod = CanonicalMethodSymbol(accessorMethod);
            // 先用访问器自身作为稳定回退目标。
            var candidates = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal)
            {
                [SymbolId(accessorMethod)] = accessorMethod,
            };

            // 精确命中的访问器实现先全部收集起来。
            foreach (var exactMethod in ResolveExactMethodFallbackCandidates(accessorMethod, receiverType))
            {
                candidates[SymbolId(exactMethod)] = exactMethod;
            }

            // 静态属性或无接收者场景只保留当前候选。
            if (receiverType is null)
            {
                return candidates.Values;
            }

            var targetDeclaringType = accessorMethod.ContainingType;
            if (targetDeclaringType is not null)
            {
                var baseDefinition = accessorMethod.OriginalDefinition.OverriddenMethod ?? accessorMethod.OriginalDefinition;
                // 先在工程内声明类型里补齐重写/实现候选。
                List<INamedTypeSymbol> declaredTypesSnapshot;
                lock (_cacheGate)
                {
                    declaredTypesSnapshot = _declaredTypes.ToList();
                }

                foreach (var declaredType in declaredTypesSnapshot)
                {
                    if (!InheritsFrom(declaredType, targetDeclaringType) ||
                        !InheritsFrom(declaredType, receiverType))
                    {
                        continue;
                    }

                    foreach (var member in declaredType.GetMembers(accessorMethod.Name).OfType<IMethodSymbol>())
                    {
                        var canonicalMember = CanonicalMethodSymbol(member);
                        if (!MethodSignatureMatches(member, accessorMethod) ||
                            !CanDispatchToCandidate(canonicalMember, accessorMethod, baseDefinition, declaredType, receiverType))
                        {
                            continue;
                        }

                        candidates[SymbolId(canonicalMember)] = canonicalMember;
                    }
                }

                // 再沿接收者继承链补齐外部或上层访问器实现。
                foreach (var superType in EnumerateBaseTypes(receiverType))
                {
                    foreach (var member in superType.GetMembers(accessorMethod.Name).OfType<IMethodSymbol>())
                    {
                        var canonicalMember = CanonicalMethodSymbol(member);
                        if (!MethodSignatureMatches(member, accessorMethod) ||
                            !CanDispatchToCandidate(canonicalMember, accessorMethod, baseDefinition, superType, receiverType))
                        {
                            continue;
                        }

                        candidates[SymbolId(canonicalMember)] = canonicalMember;
                    }
                }
            }

            // 最后再补同签名父类访问器回退。
            foreach (var superMethod in ResolveSuperTypeFallbackCandidates(accessorMethod, receiverType))
            {
                candidates[SymbolId(superMethod)] = superMethod;
            }

            return candidates.Values;
        }

        private IEnumerable<IMethodSymbol> PreferCallTargets(IEnumerable<IMethodSymbol> methods, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            var materialized = methods.ToList();
            var exactInternalMethods = materialized
              .Where(method => IsInternalMethod(method) &&
                               string.Equals(ComposeMethodFullName(method), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
              .ToList();
            if (exactInternalMethods.Count > 0)
            {
                return RankCallTargets(exactInternalMethods, fallbackTarget, receiverType);
            }

            var internalMethods = materialized.Where(IsInternalMethod).ToList();
            if (internalMethods.Count > 0)
            {
                return RankCallTargets(internalMethods, fallbackTarget, receiverType);
            }

            var exactExternalMethods = materialized
              .Where(method => !IsInternalMethod(method) &&
                               string.Equals(ComposeMethodFullName(method), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
              .ToList();
            if (exactExternalMethods.Count > 0)
            {
                return RankCallTargets(exactExternalMethods, fallbackTarget, receiverType);
            }

            var externalMethods = materialized.Where(method => !IsInternalMethod(method)).ToList();
            if (externalMethods.Count > 0)
            {
                return RankCallTargets(externalMethods, fallbackTarget, receiverType);
            }

            return new[] { fallbackTarget };
        }

        private List<IMethodSymbol> ResolvePreferredCallTargets(IEnumerable<IMethodSymbol> methods, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            var materialized = methods.ToList();
            if (materialized.Count == 0)
            {
                materialized.Add(fallbackTarget);
            }

            var preferredTargets = PreferCallTargets(materialized, fallbackTarget, receiverType).ToList();
            return preferredTargets.Count > 0 ? preferredTargets : materialized;
        }

        private IEnumerable<IMethodSymbol> RankCallTargets(IEnumerable<IMethodSymbol> methods, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            return methods
              .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
              .OrderByDescending(method => CallTargetScore(method, fallbackTarget, receiverType))
              .ThenBy(method => ComposeMethodFullName(method), StringComparer.Ordinal)
              .ToList();
        }

        private int CallTargetScore(IMethodSymbol candidate, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            var score = 0;
            // 工程内方法优先，尽量把调用边落到可分析的内部节点。
            if (IsInternalMethod(candidate))
            {
                score += 1000;
            }

            // 精确 full name 命中优先级最高，通常代表 Roslyn 原解析结果未漂移。
            //
            // ⚠ 曾存在两段「full name +500」与「name:signature +250」的评分。
            //   声明类型并入查找键后两者同键、恒同时命中，故合并为一次比较、一次 +750——
            //   **评分数值与排序完全不变**，只是不再对同一字符串重复拼接
            //   （ComposeTypeFullName 未缓存，每调用一次即一次 ToDisplayString，
            //   原先每个候选要付 4 次）。
            if (string.Equals(
              ComposeMethodFullName(candidate),
              ComposeMethodFullName(fallbackTarget),
              StringComparison.Ordinal))
            {
                score += 750;
            }

            // 原始定义一致说明它与回退目标处在同一 override/泛型族里。
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, fallbackTarget.OriginalDefinition))
            {
                score += 200;
            }

            // override 链命中次于原始定义直接命中。
            if (candidate.OverriddenMethod is not null &&
                SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, fallbackTarget.OriginalDefinition))
            {
                score += 150;
            }

            // 属性访问器额外看属性签名和接收者类型，减少 getter/setter 误配。
            if (candidate.AssociatedSymbol is IPropertySymbol candidateProperty &&
                fallbackTarget.AssociatedSymbol is IPropertySymbol fallbackProperty)
            {
                if (string.Equals(ComposePropertySignature(candidateProperty), ComposePropertySignature(fallbackProperty), StringComparison.Ordinal) &&
                    string.Equals(candidateProperty.Name, fallbackProperty.Name, StringComparison.Ordinal))
                {
                    score += 180;
                }

                if (receiverType is INamedTypeSymbol propertyReceiverType && candidateProperty.ContainingType is not null)
                {
                    if (SymbolEqualityComparer.Default.Equals(candidateProperty.ContainingType, propertyReceiverType))
                    {
                        score += 90;
                    }
                    else if (InheritsFrom(propertyReceiverType, candidateProperty.ContainingType))
                    {
                        score += 60;
                    }
                }
            }

            // 接收者类型越接近候选声明类型，分派命中概率越高。
            if (receiverType is INamedTypeSymbol namedReceiverType && candidate.ContainingType is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.ContainingType, namedReceiverType))
                {
                    score += 120;
                }
                else if (InheritsFrom(namedReceiverType, candidate.ContainingType))
                {
                    score += 80;
                }
            }

            // 扩展方法保留较低附加分，避免压过普通实例方法。
            if (candidate.IsExtensionMethod)
            {
                score += 20;
            }

            return score;
        }

        private void RegisterMethodSymbol(IMethodSymbol methodSymbol)
        {
            var canonicalMethod = CanonicalMethodSymbol(methodSymbol);
            // 归并 reducer 会写入该查找表，而 worker 同时读取它；
            // 写入必须与读取处于同一临界区。锁可重入，嵌套在 GetOrCreateSymbolNode 内亦安全。
            //
            // 历史上这里还并列写入一张 _methodSymbolsByNameAndSignature（键为「名字:签名」）。
            // 声明类型并入查找键后，该键与 ComposeMethodFullName **同键**，
            // 两张表内容完全重复，故已删除该冗余表，只保留本表。
            lock (_cacheGate)
            {
                RegisterMethodLookup(_methodSymbolsByFullName, ComposeMethodFullName(canonicalMethod), canonicalMethod);
            }
        }

        private static void RegisterMethodLookup(Dictionary<string, List<IMethodSymbol>> methodLookup, string key, IMethodSymbol methodSymbol)
        {
            if (!methodLookup.TryGetValue(key, out var methods))
            {
                methods = new List<IMethodSymbol>();
                methodLookup[key] = methods;
            }

            if (!methods.Any(existing => SymbolEqualityComparer.Default.Equals(existing, methodSymbol)))
            {
                methods.Add(methodSymbol);
            }
        }

        private IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(ITypeSymbol typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType)
            {
                return Enumerable.Empty<INamedTypeSymbol>();
            }

            var cacheKey = ComposeTypeFullName(namedType);
            if (_baseTypeCache.TryGetValue(cacheKey, out var cachedTypes))
            {
                return cachedTypes;
            }

            // 先收接口，再收基类，输出顺序保持稳定。
            var collectedTypes = new List<INamedTypeSymbol>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var interfaceType in namedType.AllInterfaces)
            {
                AddBaseType(interfaceType, collectedTypes, seen);
            }

            for (var current = namedType.BaseType; current is not null; current = current.BaseType)
            {
                AddBaseType(current, collectedTypes, seen);
            }

            _baseTypeCache[cacheKey] = collectedTypes;
            return collectedTypes;
        }

        private void AddBaseType(INamedTypeSymbol baseType, List<INamedTypeSymbol> collectedTypes, HashSet<string> seen)
        {
            var key = ComposeTypeFullName(baseType);
            if (seen.Add(key))
            {
                collectedTypes.Add(baseType);
            }
        }

        private bool MethodSignatureMatches(IMethodSymbol candidate, IMethodSymbol targetMethod)
        {
            if (!string.Equals(ComposeMethodName(candidate), ComposeMethodName(targetMethod), StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
              ComposeMethodSignature(candidate),
              ComposeMethodSignature(targetMethod),
              StringComparison.Ordinal);
        }

        private static bool CanDispatchToCandidate(IMethodSymbol candidate, IMethodSymbol targetMethod, IMethodSymbol baseDefinition, INamedTypeSymbol candidateType, ITypeSymbol receiverType)
        {
            // 候选声明类型必须先与实际接收者兼容。
            if (!InheritsFrom(candidateType, receiverType))
            {
                return false;
            }

            // 原始定义直接命中说明它与目标处在同一分派族。
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, targetMethod.OriginalDefinition) ||
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, baseDefinition))
            {
                return true;
            }

            // override 链命中也视为合法分派。
            if (candidate.OverriddenMethod is not null &&
                (SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
                 SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, baseDefinition)))
            {
                return true;
            }

            // 显式接口实现需要单独检查接口成员映射。
            foreach (var implementedMethod in candidate.ExplicitInterfaceImplementations)
            {
                if (SymbolEqualityComparer.Default.Equals(implementedMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
                    SymbolEqualityComparer.Default.Equals(implementedMethod.OriginalDefinition, baseDefinition))
                {
                    return true;
                }
            }

            // 隐式接口实现走 FindImplementationForInterfaceMember 分支。
            var implementation = candidateType.FindImplementationForInterfaceMember(targetMethod);
            if (implementation is IMethodSymbol implementationMethod &&
                SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, candidate.OriginalDefinition))
            {
                return true;
            }

            // 剩余虚方法族用修饰符作为最后一道宽松兜底。
            return candidate.IsVirtual || candidate.IsOverride || candidate.IsAbstract || candidate.IsSealed;
        }

        private bool CanDispatchToExtensionReceiver(IMethodSymbol methodSymbol, ITypeSymbol receiverType)
        {
            if (!methodSymbol.IsExtensionMethod || methodSymbol.Parameters.Length == 0)
            {
                return false;
            }

            var receiverParameterType = methodSymbol.Parameters[0].Type;
            return receiverParameterType switch
            {
                INamedTypeSymbol namedReceiverType => InheritsFrom(namedReceiverType, receiverType) || InheritsFrom((INamedTypeSymbol)receiverType, namedReceiverType),
                ITypeParameterSymbol => true,
                _ => string.Equals(ComposeTypeFullName(receiverParameterType), ComposeTypeFullName(receiverType), StringComparison.Ordinal),
            };
        }

        private static IMethodSymbol? ResolvePropertyAccessorMethod(IPropertyReferenceOperation propertyReference)
        {
            if (propertyReference.Parent is ISimpleAssignmentOperation assignment &&
                ReferenceEquals(assignment.Target, propertyReference))
            {
                return propertyReference.Property.SetMethod;
            }

            return propertyReference.Property.GetMethod;
        }
    }
}
