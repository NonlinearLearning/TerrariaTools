using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将不可达方法的阶段事实转换为方法声明删除决策。
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreachableMethodDeletion)]
public sealed class UnreachableMethodProposalRule : RuleDefinitionPropose
{
    private static readonly RuleConsumesContract UnreachableMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          UnreachableMethodFacts.Lifted)
      });


    public override string RuleId { get; } = "propose.unreachable-method";

    public override RuleConsumesContract Consumes => UnreachableMethodConsumes;

    public override string Name { get; } = "Match unreachable methods by graph reachability proposal";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
      Array.Empty<SyntaxKind>();

    // 为提升阶段已经证明不可达的方法生成删除决策。
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
            if (liftedMark.Mark.FactKind != UnreachableMethodFacts.Lifted ||
                liftedMark.Payload is not MethodDeletionLiftPayload
                {
                    Kind: MethodDeletionKind.Unreachable,
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
