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
public sealed class InterfacePropertyProposalRule : DeclarationHostProposalRuleBase
{

    public override string RuleId { get; } = "propose.type.interface-property";


    public override string Name { get; } = "Delete interface properties whose signature references the delete-class target";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[]
      {
        SyntaxKind.PropertyDeclaration
      };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为接口属性签名中的目标类引用直接生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeclarationHostProposalHelpers.EnumeratePayloads(
                     propagatedMarks,
                     DeclarationHostKind.InterfaceProperty))
        {
            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              payload.HostDeclaration,
              "Interface property signature references the delete-class target.",
              payload.HostDeclaration);
        }
    }
}

