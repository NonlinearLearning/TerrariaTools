using NLISSN.Core.Marking;

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
  string SemanticTag)
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
        var semanticTag = mark.SemanticTag?.Value ?? string.Empty;
        return new PropagationFactKey(
          ruleId,
          syntaxNode.SyntaxTree.FilePath ?? string.Empty,
          syntaxNode.SpanStart,
          syntaxNode.Span.Length,
          syntaxNode.RawKind,
          semanticTag);
    }
}
