using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// <summary>Builds structural control conclusions only after their required expressions are covered.</summary>
public sealed class ControlStructureLiftingRule : RuleDefinitionLift
{
  private static readonly RuleSemanticTag ControlStructureSemanticTag = RuleFactPorts.LiftControlStructure;

  public override string CapabilityId { get; } = "lift.target.control-structure";

  public override string RuleId { get; } = "DEL-SOBJ-LIFT-CONTROL-001";

  public override RuleConsumesContract Consumes => new(new[]
  {
    new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, RuleFactPorts.TargetExpression),
    new RuleConsumedSyntax(ExpressionFlowPropagationRuleBase.AssignmentTargetNodeKinds, RuleFactPorts.FlowAssignmentTarget),
    new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactPorts.FlowLocalDefinition),
    new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference),
    new RuleConsumedSyntax(LiftingCommon.AllowedLiftNodeKinds, RuleFactPorts.LiftExpressionHost)
  });

  public override RuleProducesContract Produces => new(
  [
    new RuleProducedSyntax(
    [
      SyntaxKind.ForStatement,
      SyntaxKind.ForEachStatement,
      SyntaxKind.ForEachVariableStatement,
      SyntaxKind.WhileStatement,
      SyntaxKind.DoStatement,
      SyntaxKind.ReturnStatement
    ], ControlStructureSemanticTag)
  ]);

  public override string Name { get; } = "Lift fully covered s-object loop and return structures";

  public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
  [
    SyntaxKind.ForStatement,
    SyntaxKind.ForEachStatement,
    SyntaxKind.ForEachVariableStatement,
    SyntaxKind.WhileStatement,
    SyntaxKind.DoStatement,
    SyntaxKind.ReturnStatement
  ];

  public override IEnumerable<LiftedMarkRecord> Lift(
    ILiftRuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
  {
    return Lift(context, seedMarks, propagatedMarks, Array.Empty<LiftedMarkRecord>());
  }

  public override IEnumerable<LiftedMarkRecord> Lift(
    ILiftRuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
    IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
  {
    var marks = seedMarks
      .Concat(propagatedMarks
        .Where(mark => !LiftingCommon.IsSymbolReferencePropagation(mark.Mark))
        .Select(mark => mark.Mark))
      .Concat(existingLiftedMarks
        .Where(mark => !LiftingCommon.IsSymbolReferencePropagation(mark.Mark))
        .Select(mark => mark.Mark))
      .ToList();
    var candidates = marks
      .SelectMany(mark => mark.SyntaxNode.AncestorsAndSelf())
      .OfType<StatementSyntax>()
      .DistinctBy(LiftingCommon.BuildNodeKey);

    foreach (var candidate in candidates)
    {
      if (!TryGetRequiredExpressions(context, candidate, out var requiredExpressions) ||
          requiredExpressions.Count == 0 ||
          !requiredExpressions.All(expression => MarkCoverage.IsCovered(expression, marks)))
      {
        continue;
      }

      var source = marks.First(mark => candidate.Span.Contains(mark.SyntaxNode.Span));
      yield return new LiftedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(RuleId, candidate, $"All required {candidate.Kind()} expressions are marked."),
        source,
        1,
        GetStructureKind(candidate));
    }
  }

  private static bool TryGetRequiredExpressions(
    ILiftRuleContext context,
    StatementSyntax candidate,
    out IReadOnlyList<ExpressionSyntax> requiredExpressions)
  {
    requiredExpressions = candidate switch
    {
      ReturnStatementSyntax { Expression: not null } statement => [statement.Expression],
      WhileStatementSyntax statement => [statement.Condition],
      DoStatementSyntax statement => [statement.Condition],
      ForEachStatementSyntax statement => [statement.Expression],
      ForEachVariableStatementSyntax statement => [statement.Expression],
      ForStatementSyntax statement => statement.Initializers
        .Concat(statement.Condition is null ? [] : [statement.Condition])
        .Concat(statement.Incrementors)
        .Concat(statement.Declaration?.Variables
          .Where(variable => variable.Initializer is not null)
          .Select(variable => variable.Initializer!.Value) ?? [])
        .ToList(),
      _ => []
    };

    if (candidate is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or
        WhileStatementSyntax or DoStatementSyntax)
    {
      _ = context.AnalyzeLoopStructure(candidate);
    }

    return candidate is ReturnStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
      ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
  }

  private static StructuralKind GetStructureKind(StatementSyntax candidate)
  {
    return candidate is ReturnStatementSyntax ? StructuralKind.Return : StructuralKind.Loop;
  }
}
