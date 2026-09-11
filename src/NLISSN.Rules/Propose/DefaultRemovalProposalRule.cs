using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 为没有更专门语法宿主的有效标记生成默认删除决策。
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class DefaultRemovalProposalRule : RuleDefinitionPropose
{
    private static readonly RuleConsumesContract TargetFactsConsumes =
      TargetProposalContracts.CreateFactsConsumes();


    public override string RuleId { get; } = "propose.default-removal";

    public override RuleConsumesContract Consumes => TargetFactsConsumes;


    public override string Name { get; } = "Match s-rooted default delete decisions";

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> DecisionConflictNodeKinds =>
      ProposalHelpers.DefaultConflictNodeKinds;

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> MergeableNodeKinds =>
      ProposalHelpers.MergeableNodeKinds;

    // 为没有被逻辑、if 或控制结构专门规则接管的剩余 mark 生成默认删除决策。
    public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        foreach (var (mark, sourceMark) in ProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(mark) ||
                mark.FactKind == RuleFactKind.FlowUnaryExpression ||
                IsTerminalTopologyInput(context, mark.SyntaxNode))
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
            if (IsHandledBySpecializedRule(seedMark) || IsTerminalTopologyInput(context, seedMark.SyntaxNode))
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
          kind is SyntaxKind.SwitchSection or SyntaxKind.SwitchStatement ||
          mark.SyntaxNode is ElseClauseSyntax;
    }

    private static bool IsTerminalTopologyInput(IProposeRuleContext context, Microsoft.CodeAnalysis.SyntaxNode syntaxNode) =>
      context is not null && syntaxNode is ExpressionSyntax expression &&
        context.ResolveExpressionTopology(expression).Termination ==
        NLISSN.Core.Analysis.ExpressionPropagation.ExpressionTopologyTermination.Terminal;

}

internal static class TargetProposalContracts
{
    private static readonly IReadOnlyList<SyntaxKind> UnaryExpressionNodeKinds = new[]
    {
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.BitwiseNotExpression,
        SyntaxKind.PreIncrementExpression,
        SyntaxKind.PreDecrementExpression,
        SyntaxKind.PostIncrementExpression,
        SyntaxKind.PostDecrementExpression,
        SyntaxKind.AddressOfExpression,
        SyntaxKind.AwaitExpression,
        SyntaxKind.SuppressNullableWarningExpression
    };

    public static RuleConsumesContract CreateFactsConsumes()
    {
        return new RuleConsumesContract(new[]
        {
            new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, RuleFactKind.TargetExpression),
            new RuleConsumedSyntax(new[] { SyntaxKind.ClassDeclaration }, RuleFactKind.TargetDeclaration),
            new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactKind.FlowAssignmentTarget),
            new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactKind.FlowLocalDefinition),
            new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactKind.FlowSymbolReference),
            new RuleConsumedSyntax(UnaryExpressionNodeKinds, RuleFactKind.FlowUnaryExpression),
            new RuleConsumedSyntax(LiftingCommon.AllowedLiftNodeKinds, RuleFactKind.LiftExpressionHost),
            new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause }, RuleFactKind.LiftIfStructure)
        });
    }
}
