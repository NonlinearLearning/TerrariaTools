using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class ExtensionReceiverNonFirstParameterShrinkProposalRule : ExtensionMethodParameterUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.extension-receiver-non-first-parameter-shrink";


    public override string Name { get; } = "Shrink non-receiver extension-method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 保持扩展方法接收者不变，只收缩非首个目标参数及其映射调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in ExtensionMethodUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks))
        {
            if (!ExtensionMethodUsageProposalHelpers.TryBuildReplacement(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Extension method non-receiver parameter type references the delete-class target; shrink the signature and keep the receiver.");

            foreach (var decision in ExtensionMethodUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Extension method invocation removes the deleted class type argument while preserving the receiver."))
            {
                yield return decision;
            }
        }
    }
}

