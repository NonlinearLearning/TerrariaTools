using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Decision;

public static class DeleteClassDelegateUsageProposalHelpers
{
    public static IEnumerable<DelegateUsagePayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, DelegateUsageMode mode)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Mark.SyntaxNode is not DelegateDeclarationSyntax ||
                propagatedMark.Payload is not DelegateUsagePayload payload ||
                payload.Mode != mode)
            {
                continue;
            }

            var key = DecisionCpgFactory.BuildNodeKey(payload.DelegateDeclaration);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            yield return payload;
        }
    }

    public static bool TryBuildReplacement(DelegateUsagePayload payload, out DelegateDeclarationSyntax replacementDelegate)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementDelegate(
          payload.DelegateDeclaration,
          payload.Parameter,
          out replacementDelegate);
    }
}

public static class DeleteClassExtensionMethodUsageProposalHelpers
{
    public static IEnumerable<ExtensionMethodMappedCallsitePayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Mark.SyntaxNode is not MethodDeclarationSyntax ||
                propagatedMark.Payload is not ExtensionMethodMappedCallsitePayload payload)
            {
                continue;
            }

            var key = DecisionCpgFactory.BuildNodeKey(payload.Method);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            yield return payload;
        }
    }

    public static bool TryBuildReplacement(ExtensionMethodMappedCallsitePayload payload, out MethodDeclarationSyntax replacementMethod)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementMethod(
          payload.Method,
          payload.Parameter,
          out replacementMethod);
    }

    public static IEnumerable<DecisionUnit> CreateInvocationReplaceDecisions(string ruleId, Compilation compilation, ExtensionMethodMappedCallsitePayload payload, string reason)
    {
        if (!TryResolveParameterSymbol(compilation, payload, out var parameterSymbol))
        {
            yield break;
        }

        foreach (var invocation in payload.InvocationCallsites)
        {
            var model = compilation.GetSemanticModel(invocation.SyntaxTree);
            if (!DeleteClassParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                  invocation,
                  model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                  parameterSymbol,
                  out var replacementInvocation))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateInvocationReplaceDecision(
              ruleId,
              invocation,
              replacementInvocation,
              reason);
        }
    }

    private static bool TryResolveParameterSymbol(Compilation compilation, ExtensionMethodMappedCallsitePayload payload, out IParameterSymbol parameterSymbol)
    {
        parameterSymbol = null!;
        var model = compilation.GetSemanticModel(payload.Method.SyntaxTree);
        if (model.GetDeclaredSymbol(payload.Method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            payload.ParameterIndex >= methodSymbol.Parameters.Length)
        {
            return false;
        }

        parameterSymbol = methodSymbol.Parameters[payload.ParameterIndex];
        return true;
    }
}
