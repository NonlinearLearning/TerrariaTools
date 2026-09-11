using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;

namespace NLISSN.Rules;

/// 将传播后的完整控制结构标记转换为删除决策；只接收声明的冲突节点种类。
public sealed class ControlStructureRemovalProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.control-structure-removal";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-CTRL-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted control structure delete decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.ControlConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override RuleTransformationContract TransformationContract { get; } = new(
      "rewrite.control-structure",
      new HashSet<DecisionActionKind> { DecisionActionKind.Delete },
      new HashSet<RewriteControlFlowEffect>
      {
          RewriteControlFlowEffect.RemoveLoop,
          RewriteControlFlowEffect.RemoveSwitchSection
      },
      true,
      false);

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

            var decision = DeleteDecisionFactory.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
            var effect = GetEffect(kind);
            yield return effect is null
              ? decision
              : decision with
              {
                  TransformationContract = TransformationContract,
                  ControlFlowEffects = new[] { effect.Value },
                  PreservedSpans = GetFollowingStatementSpans(mark.SyntaxNode)
              };
        }
    }

    private static RewriteControlFlowEffect? GetEffect(SyntaxKind kind)
    {
        return kind switch
        {
            SyntaxKind.ForStatement or SyntaxKind.WhileStatement or SyntaxKind.DoStatement => RewriteControlFlowEffect.RemoveLoop,
            SyntaxKind.SwitchStatement => RewriteControlFlowEffect.RemoveSwitchSection,
            _ => null
        };
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> GetFollowingStatementSpans(Microsoft.CodeAnalysis.SyntaxNode node)
    {
        if (node.Parent is not BlockSyntax block || node is not StatementSyntax statement)
        {
            return Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
        }

        var index = block.Statements.IndexOf(statement);
        return index >= 0 && index + 1 < block.Statements.Count
          ? new[] { block.Statements[index + 1].Span }
          : Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
    }
}
