using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 汇总委托签名收缩需要同时改写的方法组、lambda 与调用链，缺少任一可证明映射即放弃。
public static class DelegateUsageProposalHelpers
{
    // 提取指定委托使用模式下的唯一 payload，供委托收缩提案逐个消费。
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

    // 尝试删除委托声明中的目标参数，供后续同时改写方法组、lambda 或调用链。
    public static bool TryBuildReplacement(DelegateUsagePayload payload, out DelegateDeclarationSyntax replacementDelegate)
    {
        return ParameterShrinkAnalyzer.TryBuildReplacementDelegate(
          payload.DelegateDeclaration,
          payload.Parameter,
          out replacementDelegate);
    }
}

/// 收集扩展方法参数删除所需的调用点事实；扩展接收者由专门规则处理。
public static class ExtensionMethodUsageProposalHelpers
{
    // 提取扩展方法非接收者参数删除的唯一 payload，供声明与调用点同步改写。
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

    // 尝试生成删除目标参数后的扩展方法声明替换节点。
    public static bool TryBuildReplacement(ExtensionMethodMappedCallsitePayload payload, out MethodDeclarationSyntax replacementMethod)
    {
        return ParameterShrinkAnalyzer.TryBuildReplacementMethod(
          payload.Method,
          payload.Parameter,
          out replacementMethod);
    }

    // 为每个映射调用点生成删除目标参数后的调用替换决策。
    public static IEnumerable<DecisionUnit> CreateInvocationReplaceDecisions(string ruleId, Compilation compilation, ExtensionMethodMappedCallsitePayload payload, string reason)
    {
        if (!TryResolveParameterSymbol(compilation, payload, out var parameterSymbol))
        {
            yield break;
        }

        foreach (var invocation in payload.InvocationCallsites)
        {
            var model = compilation.GetSemanticModel(invocation.SyntaxTree);
            if (!ParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                  invocation,
                  model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                  parameterSymbol,
                  out var replacementInvocation))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateInvocationReplaceDecision(
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
