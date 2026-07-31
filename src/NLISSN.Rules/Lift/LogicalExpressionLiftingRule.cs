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
public sealed class LogicalExpressionLiftingRule : RuleDefinitionLift
{
  private static readonly RuleSemanticTag LogicalReductionSemanticTag = RuleFactPorts.LiftLogicalReduction;

  public override string RuleId { get; } = "DEL-SOBJ-LIFT-LOGIC-001";

  public override string CapabilityId { get; } = "lift.atomic.logical-expression";

  public override string Name { get; } = "Lift s-object marks into logical expression reductions";

  public override RuleConsumesContract Consumes { get; } = new(new[]
  {
    new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, RuleFactPorts.TargetExpression),
    new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactPorts.FlowAssignmentTarget),
    new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactPorts.FlowLocalDefinition),
    new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference)
  });

  public override RuleProducesContract Produces { get; } = new(new[]
  {
    new RuleProducedSyntax(
      new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression },
      LogicalReductionSemanticTag)
  });

  public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
    new[] { SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression };

  public override IEnumerable<LiftedMarkRecord> Lift(
    ILiftRuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
  {
    var marks = seedMarks.Concat(propagatedMarks.Select(mark => mark.Mark)).ToList();
    foreach (var host in marks.SelectMany(mark => mark.SyntaxNode.AncestorsAndSelf().OfType<BinaryExpressionSyntax>())
               .Where(host => host.IsKind(SyntaxKind.LogicalAndExpression) || host.IsKind(SyntaxKind.LogicalOrExpression))
               .DistinctBy(LiftingCommon.BuildNodeKey))
    {
      var payload = LogicalExpressionLiftingHelpers.TryBuildPayload(host, marks.Select(mark => mark.SyntaxNode));
      if (payload is null)
      {
        continue;
      }

      var sourceMark = marks.First(mark => host.Span.Contains(mark.SyntaxNode.Span));
      yield return new LiftedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(RuleId, host, "Logical operands are reducible from token-level marks."),
        sourceMark,
        1,
        Payload: payload);
    }
  }
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
