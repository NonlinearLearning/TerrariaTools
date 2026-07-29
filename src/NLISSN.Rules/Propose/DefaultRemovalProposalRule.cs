using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为没有更专门语法宿主的有效标记生成默认删除决策。
public sealed class DefaultRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleTerminalConsumesContract AllSObjectFacts = new(
      new[]
      {
        new RuleTerminalFactSelector(
          RuleFactDomain.SObject,
          new[] { RuleKind.Mark, RuleKind.Propagate, RuleKind.Lift })
      });

    public override string CapabilityId { get; } = "propose.default-removal";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-DEFAULT-001";

    public override RuleTerminalConsumesContract TerminalConsumes => AllSObjectFacts;

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted default delete decisions";

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.DefaultConflictNodeKinds;

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 为没有被逻辑、if 或控制结构专门规则接管的剩余 mark 生成默认删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(mark))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }

        foreach (var seedMark in DeleteSObjectProposalHelpers.EnumerateUncoveredSeedMarks(
                     seedMarks,
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(seedMark))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }
    }

    private static bool IsHandledBySpecializedRule(MarkRecord mark)
    {
        var kind = (Microsoft.CodeAnalysis.CSharp.SyntaxKind)mark.SyntaxNode.RawKind;
        return DeleteSObjectProposalHelpers.LogicalConflictNodeKinds.Contains(kind) ||
          DeleteSObjectProposalHelpers.IfConflictNodeKinds.Contains(kind) ||
          DeleteSObjectProposalHelpers.ControlConflictNodeKinds.Contains(kind) ||
          mark.SyntaxNode is ElseClauseSyntax;
    }
}
