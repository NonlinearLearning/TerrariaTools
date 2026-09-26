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

            // 再补同 full name 或同 name+signature 命中的精确回退候选。
            foreach (var exactMethod in ResolveExactMethodFallbackCandidates(targetMethod))
            {
                candidates[SymbolId(exactMethod)] = exactMethod;
            }

            var receiverType = invocationOperation.Instance?.Type;
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

        private IEnumerable<IMethodSymbol> ResolveExactMethodFallbackCandidates(IMethodSymbol targetMethod)
        {
            var fullName = ComposeMethodFullName(targetMethod);
            var nameAndSignatureKey = ComposeMethodLookupKey(targetMethod);
            var candidates = new List<IMethodSymbol>();
            lock (_cacheGate)
            {
                if (_methodSymbolsByFullName.TryGetValue(fullName, out var methodsByFullName))
                {
                    candidates.AddRange(methodsByFullName);
                }

                if (_methodSymbolsByNameAndSignature.TryGetValue(nameAndSignatureKey, out var methodsByNameAndSignature))
                {
                    candidates.AddRange(methodsByNameAndSignature);
                }
            }

            return candidates;
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
            List<IMethodSymbol> extensionCandidates;
            lock (_cacheGate)
            {
                extensionCandidates = _methodSymbolsByNameAndSignature.Values
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
            foreach (var exactMethod in ResolveExactMethodFallbackCandidates(accessorMethod))
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

        private static IEnumerable<IMethodSymbol> PreferCallTargets(IEnumerable<IMethodSymbol> methods, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
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

        private static List<IMethodSymbol> ResolvePreferredCallTargets(IEnumerable<IMethodSymbol> methods, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            var materialized = methods.ToList();
            if (materialized.Count == 0)
            {
                materialized.Add(fallbackTarget);
            }

            var preferredTargets = PreferCallTargets(materialized, fallbackTarget, receiverType).ToList();
            return preferredTargets.Count > 0 ? preferredTargets : materialized;
        }

        private static IEnumerable<IMethodSymbol> RankCallTargets(IEnumerable<IMethodSymbol> methods, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            return methods
              .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
              .OrderByDescending(method => CallTargetScore(method, fallbackTarget, receiverType))
              .ThenBy(method => ComposeMethodFullName(method), StringComparer.Ordinal)
              .ToList();
        }

        private static int CallTargetScore(IMethodSymbol candidate, IMethodSymbol fallbackTarget, ITypeSymbol? receiverType)
        {
            var score = 0;
            // 工程内方法优先，尽量把调用边落到可分析的内部节点。
            if (IsInternalMethod(candidate))
            {
                score += 1000;
            }

            // 精确 full name 命中优先级最高，通常代表 Roslyn 原解析结果未漂移。
            if (string.Equals(ComposeMethodFullName(candidate), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
            {
                score += 500;
            }

            // name+signature 匹配比 full name 弱一级，但仍比普通同名候选更可信。
            if (string.Equals(ComposeMethodLookupKey(candidate), ComposeMethodLookupKey(fallbackTarget), StringComparison.Ordinal))
            {
                score += 250;
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
            // 归并 reducer 会写入这两个查找表，而 worker 同时读取它们；
            // 写入必须与读取处于同一临界区。锁可重入，嵌套在 GetOrCreateSymbolNode 内亦安全。
            lock (_cacheGate)
            {
                RegisterMethodLookup(_methodSymbolsByFullName, ComposeMethodFullName(canonicalMethod), canonicalMethod);
                RegisterMethodLookup(_methodSymbolsByNameAndSignature, ComposeMethodLookupKey(canonicalMethod), canonicalMethod);
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

        private static void AddBaseType(INamedTypeSymbol baseType, List<INamedTypeSymbol> collectedTypes, HashSet<string> seen)
        {
            var key = ComposeTypeFullName(baseType);
            if (seen.Add(key))
            {
                collectedTypes.Add(baseType);
            }
        }

        private static bool MethodSignatureMatches(IMethodSymbol candidate, IMethodSymbol targetMethod)
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

        private static bool CanDispatchToExtensionReceiver(IMethodSymbol methodSymbol, ITypeSymbol receiverType)
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
