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
public sealed class OptionalParameterDefaultedMethodShrinkProposalRule : ParameterUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.optional-parameter-defaulted-method-shrink";


    public override string Name { get; } = "Shrink optional method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration,
        SyntaxKind.InvocationExpression
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 收缩带默认值的方法参数，并只在需要时改写显式传参调用点。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.Optional))
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
              "Optional method parameter type references the delete-class target; shrink the signature and keep omitted callsites unchanged.");

            foreach (var decision in MethodParameterUsageProposalHelpers.CreateInvocationReplaceDecisions(
                         RuleId,
                         context.SemanticModel.Compilation,
                         payload,
                         "Optional parameter is explicitly passed with the deleted class type value; remove the matching argument."))
            {
                yield return decision;
            }
        }
    }
}

