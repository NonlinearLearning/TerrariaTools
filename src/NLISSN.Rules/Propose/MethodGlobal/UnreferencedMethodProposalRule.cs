using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 仅为已证明无引用的方法生成声明删除决策。
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreferencedMethodDeletion)]
public sealed class UnreferencedMethodProposalRule : RuleDefinitionPropose
{
    private static readonly RuleConsumesContract UnreferencedMethodConsumes =
      new(new[]
      {
      new RuleConsumedSyntax(
        new[] { SyntaxKind.MethodDeclaration },
        UnreferencedMethodFacts.Lifted)
      });


    public override string RuleId { get; } = "propose.unreferenced-method";

    public override RuleConsumesContract Consumes => UnreferencedMethodConsumes;


    public override string Name { get; } = "Delete unreferenced private method declarations";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
      Array.Empty<SyntaxKind>();

    // 为提升阶段已经证明无剩余引用的私有方法声明生成删除决策。
    public override IEnumerable<DecisionUnit> Propose(
      IProposeRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = propagatedMarks;

        foreach (var liftedMark in liftedMarks)
        {
            if (liftedMark.Mark.FactKind != UnreferencedMethodFacts.Lifted ||
                liftedMark.Payload is not MethodDeletionLiftPayload
                {
                    Kind: MethodDeletionKind.Unreferenced,
                    OriginalReason: var reason
                })
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              liftedMark.Mark.SyntaxNode,
              reason);
        }
    }
}
