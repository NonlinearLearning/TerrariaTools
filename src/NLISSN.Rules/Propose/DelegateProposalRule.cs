using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

public sealed class DelegateProposalRule : DeclarationHostProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.delegate";


    public override string Name { get; } = "Delete delegates whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.DelegateDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为委托返回类型上的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.DelegateReturnType))
        {
            if (payload.HostDeclaration is not DelegateDeclarationSyntax delegateDeclaration)
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              delegateDeclaration,
              "Delegate return type references the delete-class target.",
              delegateDeclaration);
        }
    }
}

