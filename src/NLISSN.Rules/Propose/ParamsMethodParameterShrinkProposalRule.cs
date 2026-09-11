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
public sealed class ParamsMethodParameterShrinkProposalRule : ParameterUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.params-method-parameter-shrink";


    public override string Name { get; } = "Shrink params method parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.MethodDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 仅在所有调用都省略 params 槽位时，收缩 params 方法声明。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in MethodParameterUsageProposalHelpers.EnumerateMethodPayloads(
                     propagatedMarks,
                     MethodParameterUsageMode.ParamsOmitted))
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
              "Params method parameter type references the delete-class target; shrink the signature when all callsites omit the params slot.");
        }
    }
}

