using Microsoft.CodeAnalysis;
using RoslynPrototype.Decision;

namespace RoslynPrototype.Decision;

public sealed record RuleDecision(
  SyntaxNode OriginalNode,
  SyntaxNode FinalNode,
  DecisionActionKind Action,
  string Reason,
  SyntaxNode? ReplacementNode = null);
