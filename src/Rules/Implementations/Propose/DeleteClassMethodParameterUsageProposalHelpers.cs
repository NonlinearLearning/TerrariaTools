using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Decision;
using RoslynPrototype.Propagation;

namespace Rules;

internal static class DeleteClassMethodParameterUsageProposalHelpers
{
    internal static IEnumerable<MethodParameterUsagePayload> EnumerateMethodPayloads(
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      MethodParameterUsageMode mode)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Mark.SyntaxNode is not MethodDeclarationSyntax ||
                propagatedMark.Payload is not MethodParameterUsagePayload payload ||
                payload.Mode != mode)
            {
                continue;
            }

            var methodKey = DecisionCpgFactory.BuildNodeKey(payload.Method);
            if (!seenKeys.Add(methodKey))
            {
                continue;
            }

            yield return payload;
        }
    }

    internal static bool TryBuildReplacementMethod(
      MethodParameterUsagePayload payload,
      out MethodDeclarationSyntax replacementMethod)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementMethod(
          payload.Method,
          payload.Parameter,
          out replacementMethod);
    }

    internal static IEnumerable<DecisionUnit> CreateInvocationReplaceDecisions(
      string ruleId,
      Compilation compilation,
      MethodParameterUsagePayload payload,
      string reason)
    {
        if (!TryResolveMethodParameterSymbol(compilation, payload, out var parameterSymbol))
        {
            yield break;
        }

        foreach (var invocation in payload.InvocationCallsites)
        {
            if (!TryBuildReplacementInvocation(
                  compilation,
                  payload,
                  invocation,
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

    private static bool TryBuildReplacementInvocation(
      Compilation compilation,
      MethodParameterUsagePayload payload,
      InvocationExpressionSyntax invocation,
      IParameterSymbol parameterSymbol,
      out InvocationExpressionSyntax replacementInvocation)
    {
        replacementInvocation = null!;
        switch (payload.Mode)
        {
            case MethodParameterUsageMode.PrivatePositional:
            case MethodParameterUsageMode.PublicPositional:
                return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementInvocation(
                  invocation,
                  payload.ParameterIndex,
                  payload.Method.ParameterList.Parameters.Count,
                  out replacementInvocation);

            case MethodParameterUsageMode.NamedArgument:
                return TryResolveInvocationOperation(compilation, invocation, out var namedInvocationOperation) &&
                  DeleteClassParameterShrinkAnalyzer.TryBuildNamedArgumentReplacementInvocation(
                    invocation,
                    namedInvocationOperation,
                    parameterSymbol,
                    out replacementInvocation);

            case MethodParameterUsageMode.Optional:
                if (!TryResolveInvocationOperation(compilation, invocation, out var optionalInvocationOperation) ||
                    !DeleteClassParameterShrinkAnalyzer.TryBuildOptionalReplacementInvocation(
                      invocation,
                      optionalInvocationOperation,
                      parameterSymbol,
                      out replacementInvocation,
                      out var changed) ||
                    !changed)
                {
                    return false;
                }

                return true;

            case MethodParameterUsageMode.ParamsOmitted:
                return false;

            default:
                return false;
        }
    }

    private static bool TryResolveMethodParameterSymbol(
      Compilation compilation,
      MethodParameterUsagePayload payload,
      out IParameterSymbol parameterSymbol)
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

    private static bool TryResolveInvocationOperation(
      Compilation compilation,
      InvocationExpressionSyntax invocation,
      out IInvocationOperation invocationOperation)
    {
        invocationOperation = compilation.GetSemanticModel(invocation.SyntaxTree)
          .GetOperation(invocation, CancellationToken.None) as IInvocationOperation
          ?? null!;
        return invocationOperation is not null;
    }
}
