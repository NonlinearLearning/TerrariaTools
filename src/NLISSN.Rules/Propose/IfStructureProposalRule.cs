using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 将 Lift 阶段确认完整的 if / else if / else 结构规约为单个改写决策。
public sealed class IfStructureProposalRule : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag IfCompletionSemanticTag = new("SObject.IfCompletion");

    private static readonly RuleConsumesContract IfCompletionConsumes =
      RuleStructureContractFactories.CreateIfCompletionConsumes(
        IfCompletionSemanticTag,
        RuleInputCardinality.All);

    public override string CapabilityId { get; } = "propose.if-structure";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-IF-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted if/elseif/else structure decisions";

    public override RuleConsumesContract Consumes => IfCompletionConsumes;

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.IfConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

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

                yield return decision;
            }
        }
    }
}
