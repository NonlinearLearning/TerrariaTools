using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Core.Decision;

/// 提取局部函数参数使用事实，并生成与声明同步的调用点改写。
public static class DeleteClassLocalFunctionUsageProposalHelpers
{
    // 提取指定局部函数参数使用模式下的唯一 payload，供局部函数收缩提案消费。
    public static IEnumerable<LocalFunctionParameterUsagePayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, LocalFunctionParameterUsageMode mode)
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

    // 尝试生成删除目标参数后的局部函数声明替换节点。
    public static bool TryBuildReplacement(LocalFunctionParameterUsagePayload payload, out LocalFunctionStatementSyntax replacementLocalFunction)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementLocalFunction(
          payload.LocalFunction,
          payload.Parameter,
          out replacementLocalFunction);
    }

    // 按 payload 的调用模式生成局部函数调用点替换决策。
    public static IEnumerable<DecisionUnit> CreateInvocationReplaceDecisions(string ruleId, Compilation compilation, LocalFunctionParameterUsagePayload payload, string reason)
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

/// 提取索引器参数使用事实，并生成与声明同步的元素访问改写。
public static class DeleteClassIndexerUsageProposalHelpers
{
    // 提取指定索引器参数使用模式下的唯一 payload，供索引器收缩提案消费。
    public static IEnumerable<IndexerParameterUsagePayload> EnumeratePayloads(IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IndexerParameterUsageMode mode)
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

    // 尝试生成删除目标参数后的索引器声明替换节点。
    public static bool TryBuildReplacement(IndexerParameterUsagePayload payload, out IndexerDeclarationSyntax replacementIndexer)
    {
        return DeleteClassParameterShrinkAnalyzer.TryBuildReplacementIndexer(
          payload.Indexer,
          payload.Parameter,
          out replacementIndexer);
    }

    // 为每个受影响的元素访问生成与索引器声明一致的替换决策。
    public static IEnumerable<DecisionUnit> CreateAccessReplaceDecisions(string ruleId, Compilation compilation, IndexerParameterUsagePayload payload, string reason)
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
