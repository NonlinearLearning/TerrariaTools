using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Decision;

internal static class DeleteClassLocalFunctionUsageProposalHelpers
{
    internal static IEnumerable<LocalFunctionParameterUsagePayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, LocalFunctionParameterUsageMode mode)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Mark.SyntaxNode is not LocalFunctionStatementSyntax ||
                propagatedMark.Payload is not LocalFunctionParameterUsagePayload payload ||
                payload.Mode != mode)
            {
                continue;
            }

            var key = DecisionCpgFactory.BuildNodeKey(payload.LocalFunction);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            yield return payload;
        }
    }

    internal static bool TryBuildReplacement(LocalFunctionParameterUsagePayload payload, out LocalFunctionStatementSyntax replacementLocalFunction)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementLocalFunction(
          payload.LocalFunction,
          payload.Parameter,
          out replacementLocalFunction);
    }

    internal static IEnumerable<DecisionUnit> CreateInvocationReplaceDecisions(string ruleId, Compilation compilation, LocalFunctionParameterUsagePayload payload, string reason)
    {
        if (!TryResolveParameterSymbol(compilation, payload, out var parameterSymbol))
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

    private static bool TryResolveParameterSymbol(Compilation compilation, LocalFunctionParameterUsagePayload payload, out IParameterSymbol parameterSymbol)
    {
        parameterSymbol = null!;
        var model = compilation.GetSemanticModel(payload.LocalFunction.SyntaxTree);
        if (model.GetDeclaredSymbol(payload.LocalFunction, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            payload.ParameterIndex >= methodSymbol.Parameters.Length)
        {
            return false;
        }

        parameterSymbol = methodSymbol.Parameters[payload.ParameterIndex];
        return true;
    }

    private static bool TryBuildReplacementInvocation(Compilation compilation, LocalFunctionParameterUsagePayload payload, InvocationExpressionSyntax invocation, IParameterSymbol parameterSymbol, out InvocationExpressionSyntax replacementInvocation)
    {
        replacementInvocation = null!;
        switch (payload.Mode)
        {
            case LocalFunctionParameterUsageMode.Positional:
                return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementInvocation(
                  invocation,
                  payload.ParameterIndex,
                  payload.LocalFunction.ParameterList.Parameters.Count,
                  out replacementInvocation);

            case LocalFunctionParameterUsageMode.NamedArgument:
                return TryResolveInvocationOperation(compilation, invocation, out var namedInvocationOperation) &&
                  DeleteClassParameterShrinkAnalyzer.TryBuildNamedArgumentReplacementInvocation(
                    invocation,
                    namedInvocationOperation,
                    parameterSymbol,
                    out replacementInvocation);

            case LocalFunctionParameterUsageMode.Optional:
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

            default:
                return false;
        }
    }

    private static bool TryResolveInvocationOperation(Compilation compilation, InvocationExpressionSyntax invocation, out IInvocationOperation invocationOperation)
    {
        invocationOperation = compilation.GetSemanticModel(invocation.SyntaxTree)
          .GetOperation(invocation, CancellationToken.None) as IInvocationOperation
          ?? null!;
        return invocationOperation is not null;
    }
}

internal static class DeleteClassIndexerUsageProposalHelpers
{
    internal static IEnumerable<IndexerParameterUsagePayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IndexerParameterUsageMode mode)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propagatedMark in propagatedMarks)
        {
            if (propagatedMark.Mark.SyntaxNode is not IndexerDeclarationSyntax ||
                propagatedMark.Payload is not IndexerParameterUsagePayload payload ||
                payload.Mode != mode)
            {
                continue;
            }

            var key = DecisionCpgFactory.BuildNodeKey(payload.Indexer);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            yield return payload;
        }
    }

    internal static bool TryBuildReplacement(IndexerParameterUsagePayload payload, out IndexerDeclarationSyntax replacementIndexer)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementIndexer(
          payload.Indexer,
          payload.Parameter,
          out replacementIndexer);
    }

    internal static IEnumerable<DecisionUnit> CreateAccessReplaceDecisions(string ruleId, Compilation compilation, IndexerParameterUsagePayload payload, string reason)
    {
        if (!TryResolveParameterSymbol(compilation, payload, out var parameterSymbol))
        {
            yield break;
        }

        foreach (var access in payload.AccessCallsites)
        {
            if (!TryBuildReplacementAccess(
                  compilation,
                  payload,
                  access,
                  parameterSymbol,
                  out var replacementAccess))
            {
                continue;
            }

            yield return DeleteClassReplaceDecisionFactory.CreateElementAccessReplaceDecision(
              ruleId,
              access,
              replacementAccess,
              reason);
        }
    }

    private static bool TryResolveParameterSymbol(Compilation compilation, IndexerParameterUsagePayload payload, out IParameterSymbol parameterSymbol)
    {
        parameterSymbol = null!;
        var model = compilation.GetSemanticModel(payload.Indexer.SyntaxTree);
        if (model.GetDeclaredSymbol(payload.Indexer, CancellationToken.None) is not IPropertySymbol indexerSymbol ||
            payload.ParameterIndex >= indexerSymbol.Parameters.Length)
        {
            return false;
        }

        parameterSymbol = indexerSymbol.Parameters[payload.ParameterIndex];
        return true;
    }

    private static bool TryBuildReplacementAccess(Compilation compilation, IndexerParameterUsagePayload payload, ElementAccessExpressionSyntax access, IParameterSymbol parameterSymbol, out ElementAccessExpressionSyntax replacementAccess)
    {
        replacementAccess = null!;
        switch (payload.Mode)
        {
            case IndexerParameterUsageMode.Positional:
                return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementElementAccess(
                  access,
                  payload.ParameterIndex,
                  payload.Indexer.ParameterList.Parameters.Count,
                  out replacementAccess);

            case IndexerParameterUsageMode.NamedArgument:
                return compilation.GetSemanticModel(access.SyntaxTree)
                         .GetOperation(access, CancellationToken.None) is IPropertyReferenceOperation propertyReference &&
                  DeleteClassParameterShrinkAnalyzer.TryBuildNamedArgumentReplacementElementAccess(
                    access,
                    propertyReference,
                    parameterSymbol,
                    out replacementAccess);

            default:
                return false;
        }
    }
}
