using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class PrivateMethodParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.private-method-parameter-shrink";


    public override string Name { get; } = "Shrink private method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 同步收缩私有方法声明和所有稳定位置调用点的目标参数。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.PrivatePositional))
        {
            if (!MethodParameterUsageProposalHelpers.TryBuildReplacementMethod(
                  payload,
                  out var replacementMethod))
            {
                continue;
            }

            yield return ReplaceDecisionFactory.CreateMethodReplaceDecision(
              RuleId,
              payload.Method,
              replacementMethod,
              "Private method parameter type references the delete-class target; shrink the signature.");

            foreach (var decision in MethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Invocation passes the deleted class type argument; remove the matching positional argument."))
            {
                yield return decision;
            }
        }
    }
}

