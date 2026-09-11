using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;

namespace NLISSN.Rules;

/// 将 Lift 阶段确认完整的 if / else if / else 结构规约为单个改写决策。
public sealed class IfStructureProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.if-structure";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-IF-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted if/elseif/else structure decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.IfConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override RuleTransformationContract TransformationContract { get; } = new(
      "rewrite.if-structure",
      new HashSet<DecisionActionKind> { DecisionActionKind.Delete, DecisionActionKind.Replace },
      new HashSet<RewriteControlFlowEffect>
      {
          RewriteControlFlowEffect.RemoveThenBranch,
          RewriteControlFlowEffect.PromoteElseBranch
      },
      true,
      true);

    // 把 if 完成态 payload 规约成唯一结构决策，并记录已消费的节点避免重复产出。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;
        var consumedKeys = new HashSet<(int Start, int Length, int RawKind)>();

        foreach (var payload in DeleteSObjectProposalHelpers.EnumerateIfStructureCompletionPayloads(
                     propagatedMarks))
        {
            var decisionNode = DeleteSObjectProposalHelpers.GetIfStructureDecisionNode(payload);

            if (consumedKeys.Contains(DeleteSObjectProposalHelpers.BuildNodeKey(decisionNode)))
            {
                continue;
            }

            if (DeleteSObjectProposalHelpers.TryBuildIfStructureDecisionFromMark(
                    RuleId,
                    payload,
                    out var decision,
                    out var consumedNodes) &&
                decision is not null)
            {
                foreach (var node in consumedNodes)
                {
                    consumedKeys.Add(DeleteSObjectProposalHelpers.BuildNodeKey(node));
                }

                yield return decision with
                {
                    TransformationContract = TransformationContract,
                    ControlFlowEffects = new[] { GetEffect(payload.Kind) },
                    PreservedSpans = GetFollowingStatementSpans(decision.Fragments[0], decision.SyntaxBindings)
                };
            }
        }
    }

    private static RewriteControlFlowEffect GetEffect(IfStructureCompletionKind kind)
    {
        return kind is IfStructureCompletionKind.ReplaceIfWithElseIfTail or
            IfStructureCompletionKind.ReplaceOwningElseWithElseTail or
            IfStructureCompletionKind.ReplaceIfWithElseTail
          ? RewriteControlFlowEffect.PromoteElseBranch
          : RewriteControlFlowEffect.RemoveThenBranch;
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> GetFollowingStatementSpans(
      NLCPG.Model.NLCPGNode anchorFragment,
      IReadOnlyDictionary<NLCPG.Model.NodeId, Microsoft.CodeAnalysis.SyntaxNode> syntaxBindings)
    {
        if (!anchorFragment.NodeId.HasValue ||
            !syntaxBindings.TryGetValue(anchorFragment.NodeId.Value, out var anchorNode) ||
            anchorNode is not StatementSyntax statement ||
            anchorNode.Parent is not BlockSyntax block)
        {
            return Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
        }

        var index = block.Statements.IndexOf(statement);
        return index >= 0 && index + 1 < block.Statements.Count
          ? new[] { block.Statements[index + 1].Span }
          : Array.Empty<Microsoft.CodeAnalysis.Text.TextSpan>();
    }
}
