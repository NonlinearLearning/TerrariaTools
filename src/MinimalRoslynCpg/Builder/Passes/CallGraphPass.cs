using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes
{
internal sealed class CallGraphPass : IRoslynCpgPass
{
  internal static CallGraphPass Instance { get; } = new();

  private CallGraphPass()
  {
  }

  public string Name => nameof(CallGraphPass);

  public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
  {
    builder.RunCallGraphPass(context);
  }
}
}

namespace MinimalRoslynCpg.Builder
{
public sealed partial class RoslynCpgBuilder
{
    internal void RunCallGraphPass(RoslynCpgBuildContext context)
    {
        foreach (var invocationOperation in EnumerateOperations(context).OfType<IInvocationOperation>())
        {
            var operationNode = GetOrCreateOperationNode(invocationOperation, context.Graph);
            AddCallSite(invocationOperation, operationNode, context.Graph);
        }

        foreach (var propertyReferenceOperation in EnumerateOperations(context).OfType<IPropertyReferenceOperation>())
        {
            var operationNode = GetOrCreateOperationNode(propertyReferenceOperation, context.Graph);
            AddPropertyAccessorCallSite(propertyReferenceOperation, operationNode, context.Graph);
        }
    }

    private void AddCallSite(IInvocationOperation invocationOperation, RoslynCpgNode operationNode, RoslynCpgGraph graph)
    {
        var targetMethod = invocationOperation.TargetMethod;
        var resolvedCandidates = targetMethod is null
          ? null
          : ResolvePreferredCallTargets(ResolveCallTargetCandidates(invocationOperation, targetMethod), targetMethod, invocationOperation.Instance?.Type);
        var callSiteNode = graph.AddNode(new RoslynCpgNode(
          Kind: RoslynCpgNodeKind.CallSite,
          DisplayKind: nameof(RoslynCpgNodeKind.CallSite),
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
        graph.AddEdge(operationNode, callSiteNode, RoslynCpgEdgeKind.SyntaxChild);
        _callSiteNodesByInvocation[invocationOperation] = callSiteNode;

        if (targetMethod is not null)
        {
            foreach (var candidateMethod in resolvedCandidates!)
            {
                var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
                graph.AddEdge(callSiteNode, methodNode, RoslynCpgEdgeKind.CallTargets);
            }

            AddEvalTypeEdge(callSiteNode, targetMethod.ReturnType, graph);
        }
    }

    private RoslynCpgNode? AddPropertyAccessorCallSite(IPropertyReferenceOperation propertyReference, RoslynCpgNode operationNode, RoslynCpgGraph graph)
    {
        var accessorMethod = ResolvePropertyAccessorMethod(propertyReference);
        if (accessorMethod is null)
        {
            return null;
        }

        var resolvedCandidates = ResolvePreferredCallTargets(
          ResolveAccessorTargetCandidates(accessorMethod, propertyReference.Instance?.Type),
          accessorMethod,
          propertyReference.Instance?.Type);
        var accessorEvalType = ResolvePropertyAccessorEvalType(propertyReference, accessorMethod);
        var callSiteNode = graph.AddNode(new RoslynCpgNode(
          Kind: RoslynCpgNodeKind.CallSite,
          DisplayKind: nameof(RoslynCpgNodeKind.CallSite),
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
        graph.AddEdge(operationNode, callSiteNode, RoslynCpgEdgeKind.SyntaxChild);
        _propertyAccessorCallSiteNodesByKey[PropertyAccessorCallSiteKey(propertyReference, accessorMethod)] =
          callSiteNode;

        foreach (var candidateMethod in resolvedCandidates)
        {
            var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
            graph.AddEdge(callSiteNode, methodNode, RoslynCpgEdgeKind.CallTargets);
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
        var candidates = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal)
        {
            [SymbolId(targetMethod)] = targetMethod,
        };

        foreach (var exactMethod in ResolveExactMethodFallbackCandidates(targetMethod))
        {
            candidates[SymbolId(exactMethod)] = exactMethod;
        }

        var receiverType = invocationOperation.Instance?.Type;
        if (receiverType is null)
        {
            return candidates.Values;
        }

        var targetDeclaringType = targetMethod.ContainingType;
        if (targetDeclaringType is null)
        {
            return candidates.Values;
        }

        var baseDefinition = targetMethod.OriginalDefinition.OverriddenMethod ?? targetMethod.OriginalDefinition;

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

        foreach (var superMethod in ResolveSuperClassFallbackCandidates(targetMethod, receiverType))
        {
            candidates[SymbolId(superMethod)] = superMethod;
        }

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
        var candidates = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal)
        {
            [SymbolId(accessorMethod)] = accessorMethod,
        };

        foreach (var exactMethod in ResolveExactMethodFallbackCandidates(accessorMethod))
        {
            candidates[SymbolId(exactMethod)] = exactMethod;
        }

        if (receiverType is null)
        {
            return candidates.Values;
        }

        var targetDeclaringType = accessorMethod.ContainingType;
        if (targetDeclaringType is not null)
        {
            var baseDefinition = accessorMethod.OriginalDefinition.OverriddenMethod ?? accessorMethod.OriginalDefinition;
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
        if (IsInternalMethod(candidate))
        {
            score += 1000;
        }

        if (string.Equals(ComposeMethodFullName(candidate), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
        {
            score += 500;
        }

        if (string.Equals(ComposeMethodLookupKey(candidate), ComposeMethodLookupKey(fallbackTarget), StringComparison.Ordinal))
        {
            score += 250;
        }

        if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, fallbackTarget.OriginalDefinition))
        {
            score += 200;
        }

        if (candidate.OverriddenMethod is not null &&
            SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, fallbackTarget.OriginalDefinition))
        {
            score += 150;
        }

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
        if (!InheritsFrom(candidateType, receiverType))
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, targetMethod.OriginalDefinition) ||
            SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, baseDefinition))
        {
            return true;
        }

        if (candidate.OverriddenMethod is not null &&
            (SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
             SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, baseDefinition)))
        {
            return true;
        }

        foreach (var implementedMethod in candidate.ExplicitInterfaceImplementations)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
                SymbolEqualityComparer.Default.Equals(implementedMethod.OriginalDefinition, baseDefinition))
            {
                return true;
            }
        }

        var implementation = candidateType.FindImplementationForInterfaceMember(targetMethod);
        if (implementation is IMethodSymbol implementationMethod &&
            SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, candidate.OriginalDefinition))
        {
            return true;
        }

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
