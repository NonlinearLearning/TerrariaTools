using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public sealed class DefaultDeleteDecisionBehavior : ISyntaxNodeDecisionBehavior
{
  public int Priority => 10;

  public bool CanHandle(DecisionContext context, DecisionCandidate candidate)
  {
    return true;
  }

  public RuleDecision Decide(DecisionContext context, DecisionCandidate candidate)
  {
    var finalNode = SelectFinalNode(candidate.Mark.SyntaxNode);
    return new RuleDecision(
      candidate.Mark.SyntaxNode,
      finalNode,
      DecisionActionKind.Delete,
      BuildReason(candidate.Mark.SyntaxNode, finalNode));
  }

  private static SyntaxNode SelectFinalNode(SyntaxNode node)
  {
    if (node is MethodDeclarationSyntax) {
      return node;
    }

    if (node is ExpressionSyntax expression && RequiresStatementEscalation(expression)) {
      return (SyntaxNode?)expression.FirstAncestorOrSelf<StatementSyntax>() ?? expression;
    }

    return node;
  }

  private static bool RequiresStatementEscalation(ExpressionSyntax expression)
  {
    return expression.Parent is IfStatementSyntax
      or ForStatementSyntax
      or WhileStatementSyntax
      or DoStatementSyntax
      or ReturnStatementSyntax;
  }

  private static string BuildReason(SyntaxNode originalNode, SyntaxNode finalNode)
  {
    if (ReferenceEquals(originalNode, finalNode)) {
      return "Delete matched node.";
    }

    return $"Escalated to parent {finalNode.Kind()} for safe deletion.";
  }
}
