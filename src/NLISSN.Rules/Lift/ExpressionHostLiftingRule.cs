using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 把已传播的原子命中提升到可单独改写的表达式宿主，避免 Proposal 重复向上遍历语法树。
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class ExpressionHostLiftingRule : RuleDefinitionLift
{
    private static readonly RuleFactKind ExpressionHostFactKind = RuleFactKind.LiftExpressionHost;
    private static readonly RuleFactKind TargetFactKind = RuleFactKind.TargetExpression;
    private static readonly RuleFactKind ConditionalExpressionFlowFactKind =
      RuleFactKind.FlowConditionalExpression;
    private static readonly RuleFactKind UnaryExpressionFlowFactKind =
      RuleFactKind.FlowUnaryExpression;

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

    private static readonly RuleConsumesContract TargetFactsConsumes = new(new[]
    {
        new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, TargetFactKind),
        new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactKind.FlowAssignmentTarget),
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactKind.FlowLocalDefinition),
        new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactKind.FlowSymbolReference),
        new RuleConsumedSyntax(
          new[] { SyntaxKind.ConditionalExpression },
          ConditionalExpressionFlowFactKind),
        new RuleConsumedSyntax(UnaryExpressionNodeKinds, UnaryExpressionFlowFactKind),
    });

    private static readonly RuleProducesContract ExpressionHostProduces = new(
      new[]
      {
        new RuleProducedSyntax(
          LiftingCommon.AllowedLiftNodeKinds,
          ExpressionHostFactKind)
      });


    public override string RuleId { get; } = "lift.target.expression-host";

    public override RuleConsumesContract Consumes => TargetFactsConsumes;

    public override RuleProducesContract Produces => ExpressionHostProduces;


    public override string Name { get; } = "Lift s-object marks to direct expression and statement hosts";

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
      LiftingCommon.AllowedLiftNodeKinds;

    // 把 s-object 原子命中提升到最小可改写宿主，避免提案阶段直接操作脆弱的子表达式。
    public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var conditionalFlows = propagatedMarks
          .Where(mark => mark.Mark.FactKind == ConditionalExpressionFlowFactKind)
          .ToList();
        var conditionalHosts = conditionalFlows
          .Select(mark => mark.Mark.SyntaxNode)
          .OfType<ConditionalExpressionSyntax>()
          .DistinctBy(LiftingCommon.BuildNodeKey)
          .ToList();
        var conditionalHostMarks = conditionalFlows
          .Where(mark => mark.Mark.SyntaxNode is ConditionalExpressionSyntax)
          .GroupBy(mark => LiftingCommon.BuildNodeKey(mark.Mark.SyntaxNode))
          .Select(group => CreateConditionalHostMark(group.First()));
        var genericSeedMarks = seedMarks
          .Where(mark =>
            !IsWithinConditionalHost(mark.SyntaxNode, conditionalHosts) &&
            !IsTerminalTopologyInput(context, mark.SyntaxNode))
          .ToList();
        var genericPropagatedMarks = propagatedMarks
          .Where(mark =>
            mark.Mark.FactKind != ConditionalExpressionFlowFactKind &&
            mark.Mark.FactKind != UnaryExpressionFlowFactKind &&
            !IsWithinConditionalHost(mark.Mark.SyntaxNode, conditionalHosts) &&
            !IsTerminalTopologyFact(mark))
          .ToList();

        return conditionalHostMarks
          .Concat(ExpressionHostLiftingHelpers.BuildHostLiftedMarks(
            context,
            RuleId,
            genericSeedMarks,
            genericPropagatedMarks))
          .Select(mark => mark with
          {
            Mark = mark.Mark with
            {
              OutputKind = RuleOutputKind.ExpressionHost,
              FactKind = ExpressionHostFactKind
            }
          });
    }

    private LiftedMarkRecord CreateConditionalHostMark(PropagatedMarkRecord conditionalFlow)
    {
        return new LiftedMarkRecord(
          RuleId,
          MarkRecordFactory.Create(
            RuleId,
            conditionalFlow.Mark.SyntaxNode,
            "A marked conditional operand retains the whole conditional expression."),
          conditionalFlow.SourceMark,
          conditionalFlow.Depth + 1);
    }

    private static bool IsWithinConditionalHost(
      Microsoft.CodeAnalysis.SyntaxNode syntaxNode,
      IReadOnlyList<ConditionalExpressionSyntax> conditionalHosts)
    {
        return conditionalHosts.Any(host => host.Span.Contains(syntaxNode.Span));
    }

    private static bool IsTerminalTopologyInput(ILiftRuleContext context, Microsoft.CodeAnalysis.SyntaxNode syntaxNode) =>
      syntaxNode is ExpressionSyntax expression &&
      context.ResolveExpressionTopology(expression) is { Termination:
          NLISSN.Core.Analysis.ExpressionPropagation.ExpressionTopologyTermination.Terminal,
        StructuralOwners.Count: 0 };

    private static bool IsTerminalTopologyFact(PropagatedMarkRecord mark) =>
      mark.Payload is NLISSN.Core.Analysis.ExpressionPropagation.ExpressionTopologyPayload payload &&
      !payload.CanContinueOutward;
}
