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
public sealed class InterfaceEventProposalRule : DeclarationHostProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.interface-event";


    public override string Name { get; } = "Delete interface events whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接口事件签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceEvent))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface event signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

