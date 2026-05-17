using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace Rules;

public sealed class DeleteSObjectExpressionRule : IDeletionRule
{
  public RuleMetadata Metadata { get; } = new(
    "DEL-SOBJ-001",
    "Match s-rooted expressions",
    true);

  public IReadOnlyList<SyntaxKind> AllowedNodeKinds { get; } =
    new[]
    {
      SyntaxKind.IdentifierName,
      SyntaxKind.SimpleMemberAccessExpression,
      SyntaxKind.InvocationExpression,
      SyntaxKind.ElementAccessExpression,
      SyntaxKind.ConditionalAccessExpression,
      SyntaxKind.AddExpression,
      SyntaxKind.LogicalAndExpression,
      SyntaxKind.LogicalOrExpression,
      SyntaxKind.LocalDeclarationStatement,
      SyntaxKind.ExpressionStatement,
      SyntaxKind.IfStatement,
      SyntaxKind.ForStatement,
      SyntaxKind.WhileStatement,
      SyntaxKind.DoStatement,
      SyntaxKind.ReturnStatement
    };

  public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
  {
    if (!context.TryGetOption("target-name", out var targetName) || string.IsNullOrWhiteSpace(targetName)) {
      yield break;
    }

    foreach (var expression in root.DescendantNodes().OfType<ExpressionSyntax>()) {
      if (!IsRootedAtTarget(context, expression, targetName)) {
        continue;
      }

      if (HasRootedAncestor(context, expression, targetName)) {
        continue;
      }

      yield return new MarkRecord(
        Metadata.RuleId,
        expression,
        null,
        null,
        $"Expression is rooted at target '{targetName}'.");
    }
  }

  public IEnumerable<PropagatedMarkRecord> Propagate(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks)
  {
    foreach (var seedMark in seedMarks) {
      if (seedMark.SyntaxNode is not ExpressionSyntax expression) {
        continue;
      }

      var currentNode = expression;
      var currentDepth = 0;

      var logicalHost = FindLogicalHostExpression(currentNode);
      if (logicalHost is not null && !ReferenceEquals(logicalHost, currentNode)) {
        currentDepth++;
        currentNode = logicalHost;
        yield return new PropagatedMarkRecord(
          Metadata.RuleId,
          new MarkRecord(Metadata.RuleId, currentNode, null, null, seedMark.Reason),
          seedMark,
          currentDepth);
      }

      var structuralHost = FindStructuralHost(currentNode);
      if (structuralHost is null || ReferenceEquals(structuralHost, currentNode)) {
        continue;
      }

      yield return new PropagatedMarkRecord(
        Metadata.RuleId,
        new MarkRecord(Metadata.RuleId, structuralHost, null, null, seedMark.Reason),
        seedMark,
        currentDepth + 1);
    }
  }

  private static ExpressionSyntax? FindLogicalHostExpression(ExpressionSyntax expression)
  {
    ExpressionSyntax? logicalHost = null;

    for (var current = expression.Parent as ExpressionSyntax; current is not null; current = current.Parent as ExpressionSyntax) {
      if (current.IsKind(SyntaxKind.LogicalAndExpression) || current.IsKind(SyntaxKind.LogicalOrExpression)) {
        logicalHost = current;
        continue;
      }

      break;
    }

    return logicalHost;
  }

  private static SyntaxNode? FindStructuralHost(ExpressionSyntax expression)
  {
    return expression.Parent switch
    {
      IfStatementSyntax ifStatement when ifStatement.Condition == expression => ifStatement,
      WhileStatementSyntax whileStatement when whileStatement.Condition == expression => whileStatement,
      DoStatementSyntax doStatement when doStatement.Condition == expression => doStatement,
      ReturnStatementSyntax returnStatement when returnStatement.Expression == expression => returnStatement,
      ForStatementSyntax forStatement when forStatement.Condition == expression => forStatement,
      _ => expression.FirstAncestorOrSelf<StatementSyntax>()
    };
  }

  private static bool IsRootedAtTarget(RuleContext context, ExpressionSyntax expression, string targetName)
  {
    var operation = context.SemanticModel.GetOperation(expression);
    if (operation is null) {
      return false;
    }

    return ReferencesTarget(operation, targetName);
  }

  private static bool HasRootedAncestor(RuleContext context, ExpressionSyntax expression, string targetName)
  {
    foreach (var ancestor in expression.Ancestors().OfType<ExpressionSyntax>()) {
      if (ancestor.IsKind(SyntaxKind.LogicalAndExpression) || ancestor.IsKind(SyntaxKind.LogicalOrExpression)) {
        continue;
      }

      if (IsRootedAtTarget(context, ancestor, targetName)) {
        return true;
      }
    }

    return false;
  }

  private static bool ReferencesTarget(IOperation operation, string targetName)
  {
    if (operation is ILocalReferenceOperation localReference && localReference.Local.Name == targetName) {
      return true;
    }

    if (operation is IParameterReferenceOperation parameterReference && parameterReference.Parameter.Name == targetName) {
      return true;
    }

    foreach (var child in operation.ChildOperations) {
      if (ReferencesTarget(child, targetName)) {
        return true;
      }
    }

    return false;
  }
}
