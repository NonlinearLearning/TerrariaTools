using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

/// <summary>
/// Identifies one propagated token or relation fact independently of its provenance text and depth.
/// </summary>
internal sealed record PropagationFactKey(
  string RuleId,
  string FilePath,
  int SpanStart,
  int SpanLength,
  int RawKind,
  RuleFactKind? FactKind,
  string? SemanticTag)
{
    public static PropagationFactKey Create(PropagatedMarkRecord fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return Create(fact.RuleId, fact.Mark);
    }

    public static PropagationFactKey Create(string ruleId, MarkRecord mark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(mark);
        var syntaxNode = mark.SyntaxNode;
        var factKind = RuleFactKindDescriptor.Resolve(mark.FactKind, mark.SemanticTag);
        return new PropagationFactKey(
          ruleId,
          syntaxNode.SyntaxTree.FilePath ?? string.Empty,
          syntaxNode.SpanStart,
          syntaxNode.Span.Length,
          syntaxNode.RawKind,
          factKind,
          factKind is null ? mark.SemanticTag?.Value : null);
    }
}
