using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为没有更专门语法宿主的有效标记生成默认删除决策。
public sealed class DefaultRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleConsumesContract TargetFactsConsumes =
      TargetProposalContracts.CreateFactsConsumes();

    public override string CapabilityId { get; } = "propose.default-removal";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-DEFAULT-001";

    public override RuleConsumesContract Consumes => TargetFactsConsumes;


    public override string Name { get; } = "Match s-rooted default delete decisions";

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> DecisionConflictNodeKinds =>
      ProposalHelpers.DefaultConflictNodeKinds;

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为没有被逻辑、if 或控制结构专门规则接管的剩余 mark 生成默认删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var (mark, sourceMark) in ProposalHelpers.EnumerateActiveDerivedMarks(
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

        foreach (var seedMark in ProposalHelpers.EnumerateUncoveredSeedMarks(
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
        return ProposalHelpers.LogicalConflictNodeKinds.Contains(kind) ||
          ProposalHelpers.IfConflictNodeKinds.Contains(kind) ||
          ProposalHelpers.ControlConflictNodeKinds.Contains(kind) ||
          mark.SyntaxNode is ElseClauseSyntax;
    }
}

internal static class TargetProposalContracts
{
    public static RuleConsumesContract CreateFactsConsumes()
    {
        return new RuleConsumesContract(new[]
        {
            new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, RuleFactPorts.TargetExpression),
            new RuleConsumedSyntax(new[] { SyntaxKind.ClassDeclaration }, RuleFactPorts.TargetDeclaration),
            new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactPorts.FlowAssignmentTarget),
            new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactPorts.FlowLocalDefinition),
            new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference),
            new RuleConsumedSyntax(LiftingCommon.AllowedLiftNodeKinds, RuleFactPorts.LiftExpressionHost),
            new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause }, RuleFactPorts.LiftIfStructure)
        });
    }
}
