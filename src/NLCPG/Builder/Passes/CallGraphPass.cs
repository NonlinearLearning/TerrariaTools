using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
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
        internal void RunCallGraphPass(NLCPGBuildContext context)
        {
            // 显式方法调用会直接产出调用点和调用目标边。
            foreach (var invocationOperation in EnumerateOperations(context).OfType<IInvocationOperation>())
            {
                var operationNode = GetOrCreateOperationNode(invocationOperation, context.Graph);
                AddCallSite(invocationOperation, operationNode, context.Graph);
            }

            // 属性引用可能隐式落到 getter/setter，需要单独补调用点。
            foreach (var propertyReferenceOperation in EnumerateOperations(context).OfType<IPropertyReferenceOperation>())
            {
                var operationNode = GetOrCreateOperationNode(propertyReferenceOperation, context.Graph);
                AddPropertyAccessorCallSite(propertyReferenceOperation, operationNode, context.Graph);
            }
        }

        private void AddCallSite(IInvocationOperation invocationOperation, NLCPGNode operationNode, NLCPGGraph graph)
        {
            var targetMethod = invocationOperation.TargetMethod;
            // Roslyn 已解析到目标时，继续扩充候选集并按内部优先规则排序。
            var resolvedCandidates = targetMethod is null
              ? null
              : ResolvePreferredCallTargets(ResolveCallTargetCandidates(invocationOperation, targetMethod), targetMethod, invocationOperation.Instance?.Type);
            // 调用点节点复用操作节点的源码位置，但名字和签名以目标方法为准。
            var callSiteNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.CallSite,
              DisplayKind: nameof(NLCPGNodeKind.CallSite),
              Name: targetMethod?.Name ?? operationNode.Name,
              FullName: targetMethod is null ? operationNode.FullName : ComposeInvocationMethodFullName(targetMethod),
              Signature: targetMethod is null ? operationNode.Signature : ComposeInvocationSignature(targetMethod),
              DispatchKind: targetMethod is null
                ? null
                : ComposeResolvedDispatchKind(
                  resolvedCandidates![0],
                  targetMethod,
                  invocationOperation.Instance?.Type,
                  ComposeCallDispatchKind(resolvedCandidates[0], invocationOperation.Instance is not null)),
              TypeFullName: ComposeTypeFullName(invocationOperation.Type),
              FilePath: operationNode.FilePath,
              SpanStart: operationNode.SpanStart,
              SpanEnd: operationNode.SpanEnd));
            graph.AddEdge(operationNode, callSiteNode, NLCPGEdgeKind.SyntaxChild);
            _callSiteNodesByInvocation[invocationOperation] = callSiteNode;

            // 只有拿到目标方法时，才继续补充调用目标和求值类型。
            if (targetMethod is not null)
            {
                foreach (var candidateMethod in resolvedCandidates!)
                {
                    var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
                    graph.AddEdge(callSiteNode, methodNode, NLCPGEdgeKind.CallTargets);
                }

                AddEvalTypeEdge(callSiteNode, targetMethod.ReturnType, graph);
            }
        }

        private NLCPGNode? AddPropertyAccessorCallSite(IPropertyReferenceOperation propertyReference, NLCPGNode operationNode, NLCPGGraph graph)
        {
            var accessorMethod = ResolvePropertyAccessorMethod(propertyReference);
            // 无访问器可解析时直接退出，避免写出半截调用点。
            if (accessorMethod is null)
            {
                return null;
            }

            // 访问器候选解析与普通方法类似，但输入是属性访问器符号。
            var resolvedCandidates = ResolvePreferredCallTargets(
              ResolveAccessorTargetCandidates(accessorMethod, propertyReference.Instance?.Type),
              accessorMethod,
              propertyReference.Instance?.Type);
            var accessorEvalType = ResolvePropertyAccessorEvalType(propertyReference, accessorMethod);
            // 访问器调用点同样挂在原操作节点下，方便后续统一查询。
            var callSiteNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.CallSite,
              DisplayKind: nameof(NLCPGNodeKind.CallSite),
              Name: accessorMethod.Name,
              FullName: ComposeInvocationMethodFullName(accessorMethod),
              Signature: ComposeInvocationSignature(accessorMethod),
              DispatchKind: ComposeResolvedDispatchKind(
                resolvedCandidates[0],
                accessorMethod,
                propertyReference.Instance?.Type,
                ComposePropertyAccessorDispatchKind(resolvedCandidates[0], propertyReference.Instance is not null)),
              TypeFullName: ComposeTypeFullName(accessorEvalType),
              FilePath: operationNode.FilePath,
              SpanStart: operationNode.SpanStart,
              SpanEnd: operationNode.SpanEnd));
            graph.AddEdge(operationNode, callSiteNode, NLCPGEdgeKind.SyntaxChild);
            _propertyAccessorCallSiteNodesByKey[PropertyAccessorCallSiteKey(propertyReference, accessorMethod)] =
              callSiteNode;

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
            foreach (var superMethod in ResolveSuperClassFallbackCandidates(targetMethod, receiverType))
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

        private IEnumerable<IMethodSymbol> ResolveExactMethodFallbackCandidates(IMethodSymbol targetMethod)
        {
            var fullName = ComposeMethodFullName(targetMethod);
            if (_methodSymbolsByFullName.TryGetValue(fullName, out var methodsByFullName))
            {
                foreach (var method in methodsByFullName)
                {
                    yield return method;
                }
            }

            var nameAndSignatureKey = ComposeMethodLookupKey(targetMethod);
            if (_methodSymbolsByNameAndSignature.TryGetValue(nameAndSignatureKey, out var methodsByNameAndSignature))
            {
                foreach (var method in methodsByNameAndSignature)
                {
                    yield return method;
                }
            }
        }

        private IEnumerable<IMethodSymbol> ResolveSuperClassFallbackCandidates(IMethodSymbol targetMethod, ITypeSymbol receiverType)
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

            foreach (var methodGroup in _methodSymbolsByNameAndSignature.Values)
            {
                foreach (var method in methodGroup)
                {
                    if (!method.IsExtensionMethod)
                    {
                        continue;
                    }

                    var canonicalMethod = CanonicalMethodSymbol(method);
                    if (!MethodSignatureMatches(canonicalMethod, targetMethod) ||
                        !CanDispatchToExtensionReceiver(canonicalMethod, receiverType))
                    {
                        continue;
                    }

                    yield return canonicalMethod;
                }
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
                foreach (var declaredType in _declaredTypes)
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
            foreach (var superMethod in ResolveSuperClassFallbackCandidates(accessorMethod, receiverType))
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
            RegisterMethodLookup(_methodSymbolsByFullName, ComposeMethodFullName(canonicalMethod), canonicalMethod);
            RegisterMethodLookup(_methodSymbolsByNameAndSignature, ComposeMethodLookupKey(canonicalMethod), canonicalMethod);
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
