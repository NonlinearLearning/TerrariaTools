using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class DelegateParameterShrinkProposalRule : DelegateUsageProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.delegate-parameter-shrink";


    public override string Name { get; } = "Shrink delegate parameters whose type references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      Array.Empty<SyntaxKind>();

    // 在委托没有外部复杂绑定时，只收缩委托签名本身。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DelegateUsageProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DelegateUsageMode.PlainSignature))
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
              "Delegate parameter type references the delete-class target; shrink the signature.");
        }
    }
}

