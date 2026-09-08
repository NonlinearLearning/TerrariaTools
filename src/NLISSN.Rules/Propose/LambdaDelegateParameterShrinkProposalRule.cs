using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class LambdaDelegateParameterShrinkProposalRule : DelegateUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.lambda-delegate-parameter-shrink";


    public override string Name { get; } = "Shrink delegate parameters and lambda bindings when the delete-class target flows through a custom delegate signature";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.SimpleLambdaExpression,
        SyntaxKind.ParenthesizedLambdaExpression,
        SyntaxKind.AnonymousMethodExpression,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩委托签名以及所有 lambda 绑定与调用链。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.Lambda))
        {
            if (!DelegateUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementDelegate))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateDelegateReplaceDecision(
              RuleId,
              payload.DelegateDeclaration,
              replacementDelegate,
              "Delegate parameter type references the delete-class target; shrink the delegate and its lambda bindings.");

            foreach (var expression in payload.LambdaTargets)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(expression.SyntaxTree);
                if (model.GetOperation(expression, CancellationToken.None) is not IAnonymousFunctionOperation anonymousFunction ||
                    !ParameterShrinkAnalyzer.TryBuildLambdaRewrite(
                      context,
                      model,
                      expression,
                      anonymousFunction,
                      payload.ParameterIndex,
                      out var lambdaRewrite))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateExpressionReplaceDecision(
                  RuleId,
                  lambdaRewrite.Expression,
                  lambdaRewrite.Replacement,
                  "Lambda binding removes the deleted class type parameter to stay compatible with the shrunk delegate.");
            }

            foreach (var invocation in payload.InvocationCallsites)
            {
                var model = context.SemanticModel.Compilation.GetSemanticModel(invocation.SyntaxTree);
                if (context.SemanticModel.GetDeclaredSymbol(payload.DelegateDeclaration, CancellationToken.None) is not INamedTypeSymbol delegateSymbol ||
                    delegateSymbol.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
                    payload.ParameterIndex >= invokeMethod.Parameters.Length ||
                    !ParameterShrinkAnalyzer.TryBuildMappedInvocationReplacement(
                      invocation,
                      model.GetOperation(invocation, CancellationToken.None) as IInvocationOperation,
                      invokeMethod.Parameters[payload.ParameterIndex],
                      out var replacementInvocation))
                {
                    continue;
                }

                yield return ReplaceDecisionFactory.CreateInvocationReplaceDecision(
                  RuleId,
                  invocation,
                  replacementInvocation,
                  "Delegate invocation removes the deleted class type argument after delegate signature shrink.");
            }
        }
    }
}

