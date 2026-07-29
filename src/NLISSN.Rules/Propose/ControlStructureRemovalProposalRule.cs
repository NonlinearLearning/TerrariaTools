using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将传播后的完整控制结构标记转换为删除决策；只接收声明的冲突节点种类。
public sealed class ControlStructureRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleTerminalConsumesContract AllSObjectFacts = new(
      new[]
      {
        new RuleTerminalFactSelector(
          RuleFactDomain.SObject,
          new[] { RuleKind.Mark, RuleKind.Propagate, RuleKind.Lift })
      });

    public override string CapabilityId { get; } = "propose.control-structure-removal";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-CTRL-001";

    public override RuleTerminalConsumesContract TerminalConsumes => AllSObjectFacts;

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted control structure delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.ControlConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 把已收束到控制结构宿主的派生 mark 转成直接删除决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            var kind = (SyntaxKind)mark.SyntaxNode.RawKind;
            if (!DecisionConflictNodeKinds.Contains(kind))
            {
                continue;
            }

            yield return DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }
    }
}
