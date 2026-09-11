using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;

namespace NLISSN.Rules;

/// 为逻辑表达式的可删操作数选择保持短路语义的规约决策。
[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class LogicalExpressionProposalRule : RuleDefinitionPropose
{
    private static readonly RuleFactKind LogicalReductionFactKind = RuleFactKind.LiftLogicalReduction;

    private static readonly RuleConsumesContract LogicalHostConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression },
          LogicalReductionFactKind)
      });


    public override string RuleId { get; } = "propose.logical-expression";

    public override RuleConsumesContract Consumes => LogicalHostConsumes;


    public override string Name { get; } = "Match s-rooted logical expression reductions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      ProposalHelpers.LogicalConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    public override RuleTransformationContract TransformationContract { get; } = new(
      "rewrite.logical-condition",
      new HashSet<DecisionActionKind> { DecisionActionKind.Replace },
      new HashSet<RewriteControlFlowEffect> { RewriteControlFlowEffect.SimplifyLogicalCondition },
      true,
      false);

    // 根据逻辑宿主 payload 生成保持短路语义的 Replace 决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        foreach (var payload in ProposalHelpers.EnumerateLogicalReductionLiftPayloads(
                     liftedMarks))
        {
            if (payload.Proof is not
                {
                  Goal: CoverageGoal.LogicalReduction,
                  Status: CoverageProofStatus.Complete
                })
            {
                continue;
            }

            var replacementNode = ProposalHelpers.BuildLogicalReplacement(
              payload);
            if (replacementNode is not null)
            {
                yield return ProposalHelpers.CreateLogicalReplaceDecision(
                  RuleId,
                  payload,
                  replacementNode) with
                {
                    TransformationContract = TransformationContract,
                    ControlFlowEffects = new[] { RewriteControlFlowEffect.SimplifyLogicalCondition }
                };
            }
        }
    }
}
