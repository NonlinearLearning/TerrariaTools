using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为逻辑表达式的可删操作数选择保持短路语义的规约决策。
public sealed class LogicalExpressionProposalRule : RuleDefinitionPropose
{
    private static readonly RuleSemanticTag LogicalHostSemanticTag = new("SObject.LogicalHost");

    private static readonly RuleConsumesContract LogicalHostConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression },
          LogicalHostSemanticTag)
      });

    public override string CapabilityId { get; } = "propose.logical-expression";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-LOGIC-001";

    public override RuleConsumesContract Consumes => LogicalHostConsumes;


    public override string Name { get; } = "Match s-rooted logical expression reductions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.LogicalConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    // 根据逻辑宿主 payload 生成保持短路语义的 Replace 决策。
    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteSObjectProposalHelpers.EnumerateLogicalHostPayloads(
                     propagatedMarks))
        {
            var replacementNode = DeleteSObjectProposalHelpers.BuildLogicalReplacement(
              payload);
            if (replacementNode is not null)
            {
                yield return DeleteSObjectProposalHelpers.CreateLogicalReplaceDecision(
                  RuleId,
                  payload.Host,
                  replacementNode);
            }
        }
    }
}
