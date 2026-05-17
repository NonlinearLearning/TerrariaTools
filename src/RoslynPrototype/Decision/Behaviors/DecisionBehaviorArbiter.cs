using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public sealed class DecisionBehaviorArbiter : IDecisionBehaviorArbiter
{
  public RuleDecision Resolve(
    DecisionContext context,
    DecisionCandidate candidate,
    IReadOnlyList<RuleDecision> proposals)
  {
    if (ShouldSkipSeedLogicalHost(context, candidate)) {
      return new RuleDecision(
        candidate.Mark.SyntaxNode,
        candidate.Mark.SyntaxNode,
        DecisionActionKind.Skip,
        "Skipped because this logical expression is reduced by a propagated decision.");
    }

    if (ShouldSkipCoveredNode(context, candidate.Mark.SyntaxNode)) {
      return new RuleDecision(
        candidate.Mark.SyntaxNode,
        candidate.Mark.SyntaxNode,
        DecisionActionKind.Skip,
        "Skipped because a reducible logical expression candidate already covers this node.");
    }

    if (ShouldSkipStructuralHost(context, candidate.Mark.SyntaxNode)) {
      return new RuleDecision(
        candidate.Mark.SyntaxNode,
        candidate.Mark.SyntaxNode,
        DecisionActionKind.Skip,
        "Skipped because a reducible logical expression candidate already covers this structure.");
    }

    if (proposals.Count == 0) {
      return new RuleDecision(
        candidate.Mark.SyntaxNode,
        candidate.Mark.SyntaxNode,
        DecisionActionKind.Skip,
        "Skipped because no decision behavior handled this candidate.");
    }

    return proposals[0];
  }

  private static bool ShouldSkipSeedLogicalHost(
    DecisionContext context,
    DecisionCandidate candidate)
  {
    return !candidate.IsPropagated &&
      candidate.Mark.SyntaxNode is BinaryExpressionSyntax binaryExpression &&
      (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
       binaryExpression.IsKind(SyntaxKind.LogicalOrExpression)) &&
      context.ReducibleLogicalHosts.Contains(binaryExpression);
  }

  private static bool ShouldSkipCoveredNode(DecisionContext context, SyntaxNode node)
  {
    foreach (var ancestor in node.Ancestors().OfType<BinaryExpressionSyntax>()) {
      if (!ancestor.IsKind(SyntaxKind.LogicalAndExpression) &&
          !ancestor.IsKind(SyntaxKind.LogicalOrExpression)) {
        continue;
      }

        if (context.ReducibleLogicalHosts.Contains(ancestor)) {
          return true;
        }
    }

    return false;
  }

  private static bool ShouldSkipStructuralHost(DecisionContext context, SyntaxNode node)
  {
    foreach (var logicalHost in context.ReducibleLogicalHosts) {
      if (node.Span.Contains(logicalHost.Span) && node.Span != logicalHost.Span) {
        return true;
      }
    }

    return false;
  }
}
