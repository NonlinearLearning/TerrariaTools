using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Model;

namespace RoslynPrototype.Marking;

public sealed record MarkRecord(
  string RuleId,
  SyntaxNode SyntaxNode,
  SyntaxAnnotation? Annotation,
  RoslynCpgNode? PrimaryGraphNode,
  string Reason);
