using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public sealed class LogicalBinaryReductionBehavior : ISyntaxNodeDecisionBehavior
{
  public int Priority => 100;

  public bool CanHandle(DecisionContext context, DecisionCandidate candidate)
  {
    if (!candidate.IsPropagated || candidate.SourceMark is null) {
      return false;
    }

    if (candidate.Mark.SyntaxNode is not BinaryExpressionSyntax binaryExpression) {
      return false;
    }

    return binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
      binaryExpression.IsKind(SyntaxKind.LogicalOrExpression);
  }

  public RuleDecision Decide(DecisionContext context, DecisionCandidate candidate)
  {
    var binaryExpression = (BinaryExpressionSyntax)candidate.Mark.SyntaxNode;
    var sourceNode = candidate.SourceMark!.SyntaxNode;
    var leftContainsSource = binaryExpression.Left.Span.Contains(sourceNode.Span);
    var rightContainsSource = binaryExpression.Right.Span.Contains(sourceNode.Span);
    if (leftContainsSource == rightContainsSource) {
      throw new InvalidOperationException(
        "Logical reduction behavior requires the source node to belong to exactly one operand.");
    }

    var replacementNode = leftContainsSource ? binaryExpression.Right : binaryExpression.Left;
    return new RuleDecision(
      binaryExpression,
      binaryExpression,
      DecisionActionKind.Replace,
      $"Reduced {binaryExpression.Kind()} to the surviving operand.",
      replacementNode.WithoutTrivia());
  }
}
