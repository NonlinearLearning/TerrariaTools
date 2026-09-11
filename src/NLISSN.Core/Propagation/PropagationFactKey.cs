using NLISSN.Core.Marking;
using NLISSN.Core.Lifting;
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
  string? SemanticTag,
  string SourceTreeVersion,
  string AnchorNodeKey,
  string PayloadIdentity,
  string ProvenanceIdentity)
{
    public static PropagationFactKey Create(PropagatedMarkRecord fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var mark = fact.Mark;
        // Legacy propagation rules do not populate the dedicated provenance
        // slot. Their output Mark already carries the producer provenance;
        // using it keeps the historical fixed point stable. New producers
        // that provide a propagation provenance retain the full source path.
        var provenance = fact.Provenance ?? fact.Mark.Provenance ?? FactProvenance.ForPropagation(
          fact.RuleId,
          fact.SourceMark.Provenance,
          fact.Depth);
        return Create(
          fact.RuleId,
          mark,
          fact.Payload,
          provenance,
          mark.SourceTreeVersion);
    }

    public static PropagationFactKey Create(
      string ruleId,
      MarkRecord mark,
      object? payload = null,
      FactProvenance? provenance = null,
      string? sourceTreeVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(mark);
        var syntaxNode = mark.SyntaxNode;
        var factKind = RuleFactKindDescriptor.Resolve(mark.FactKind, mark.SemanticTag);
        var resolvedProvenance = provenance ?? mark.Provenance ?? FactProvenance.ForMark(ruleId);
        var resolvedSourceTreeVersion = sourceTreeVersion ?? mark.SourceTreeVersion;
        return new PropagationFactKey(
          ruleId,
          syntaxNode.SyntaxTree.FilePath ?? string.Empty,
          syntaxNode.SpanStart,
          syntaxNode.Span.Length,
          syntaxNode.RawKind,
          factKind,
          factKind is null ? mark.SemanticTag?.Value : null,
          resolvedSourceTreeVersion,
          FactIdentity.BuildNodeKey(syntaxNode),
          FactIdentity.ComputePayloadIdentity(payload),
          resolvedProvenance.Identity);
    }
}
