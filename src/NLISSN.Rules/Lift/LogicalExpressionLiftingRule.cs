using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// <summary>
/// Reduces a logical expression from token-level marks without classifying it as a structure.
/// </summary>
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class LogicalExpressionLiftingRule : RuleDefinitionLift
{
  private static readonly RuleFactKind LogicalReductionFactKind = RuleFactKind.LiftLogicalReduction;
  private static readonly RuleFactKind LogicalExpressionFlowFactKind = RuleFactKind.FlowLogicalExpression;
  private static readonly RuleFactKind UnaryExpressionFlowFactKind = RuleFactKind.FlowUnaryExpression;

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

  public override string RuleId { get; } = "lift.atomic.logical-expression";


  public override string Name { get; } = "Lift s-object marks into logical expression reductions";

  public override RuleConsumesContract Consumes { get; } = new(new[]
  {
    new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, RuleFactKind.TargetExpression),
    new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactKind.FlowAssignmentTarget),
    new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactKind.FlowLocalDefinition),
    new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactKind.FlowSymbolReference),
    new RuleConsumedSyntax(
      new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression },
      LogicalExpressionFlowFactKind),
    new RuleConsumedSyntax(UnaryExpressionNodeKinds, UnaryExpressionFlowFactKind)
  });

  public override RuleProducesContract Produces { get; } = new(new[]
  {
    new RuleProducedSyntax(
      new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression },
      LogicalReductionFactKind)
  });

  public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
    new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression };

  public override IEnumerable<LiftedMarkRecord> Lift(
    ILiftRuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
  {
    var operandMarks = seedMarks
      .Where(mark => IsTopologyOperandOfLogicalHost(context, mark.SyntaxNode))
      .Concat(propagatedMarks
        .Where(mark => mark.Mark.FactKind != LogicalExpressionFlowFactKind &&
          mark.Mark.FactKind != UnaryExpressionFlowFactKind &&
          !IsTerminalTopologyFact(mark))
        .Select(mark => mark.Mark))
      .ToList();
    var propagatedLogicalHosts = propagatedMarks
      .Where(mark => mark.Mark.FactKind == LogicalExpressionFlowFactKind)
      .Select(mark => mark.Mark.SyntaxNode)
      .OfType<BinaryExpressionSyntax>();
    foreach (var host in propagatedLogicalHosts
               .Where(host => host.IsKind(SyntaxKind.LogicalAndExpression) || host.IsKind(SyntaxKind.LogicalOrExpression))
               .DistinctBy(LiftingCommon.BuildNodeKey))
    {
      var payload = LogicalExpressionLiftingHelpers.TryBuildPayload(host, operandMarks.Select(mark => mark.SyntaxNode));
      if (payload is null)
      {
        continue;
      }

      var sourceMark = propagatedMarks
        .Where(mark => mark.Mark.FactKind == LogicalExpressionFlowFactKind)
        .FirstOrDefault(mark => ReferenceEquals(mark.Mark.SyntaxNode, host))
        ?.SourceMark ?? seedMarks.FirstOrDefault(mark => host.Span.Contains(mark.SyntaxNode.Span));
      if (sourceMark is null)
      {
        continue;
      }

      yield return new LiftedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(RuleId, host, "Logical operands are reducible from token-level marks."),
        sourceMark,
        1,
        Payload: payload);
    }
  }

  private static bool IsTerminalTopologyFact(PropagatedMarkRecord mark) =>
    mark.Payload is NLISSN.Core.Analysis.ExpressionPropagation.ExpressionTopologyPayload payload &&
    !payload.CanContinueOutward;

  private static bool IsTopologyOperandOfLogicalHost(ILiftRuleContext context, SyntaxNode syntaxNode) =>
    syntaxNode is ExpressionSyntax expression &&
    context.ResolveExpressionTopology(expression).Steps.Any(step =>
      step.Host is BinaryExpressionSyntax binary &&
      (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression)));

}

internal static class LogicalExpressionLiftingHelpers
{
  public static LogicalExpressionReductionPayload? TryBuildPayload(
    BinaryExpressionSyntax host,
    IEnumerable<SyntaxNode> sourceNodes)
  {
    var markedNodes = sourceNodes
      .Where(node => host.Span.Contains(node.Span))
      .OfType<ExpressionSyntax>()
      .DistinctBy(LiftingCommon.BuildNodeKey)
      .ToList();
    var operands = Flatten(host).ToList();
    var removable = operands
      .Where(operand => markedNodes.Any(marked =>
        operand.Span.Contains(marked.Span) || marked.Span.Contains(operand.Span)))
      .ToList();
    var survivors = operands.Where(operand => !removable.Contains(operand)).ToList();
    return removable.Count > 0 && survivors.Count > 0
      ? new LogicalExpressionReductionPayload(host, removable, survivors)
      : null;
  }

  private static IEnumerable<ExpressionSyntax> Flatten(BinaryExpressionSyntax expression)
  {
    if (expression.Left is BinaryExpressionSyntax left && left.Kind() == expression.Kind())
    {
      foreach (var operand in Flatten(left))
      {
        yield return operand;
      }
    }
    else
    {
      yield return expression.Left;
    }

    if (expression.Right is BinaryExpressionSyntax right && right.Kind() == expression.Kind())
    {
      foreach (var operand in Flatten(right))
      {
        yield return operand;
      }
    }
    else
    {
      yield return expression.Right;
    }
  }
}
