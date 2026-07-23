namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Materialized, method-scoped facts retained at the fragment boundary.
/// Roslyn semantic objects and graph state must not cross this boundary.
/// </summary>
internal sealed class OperationFragmentFacts
{
  private IReadOnlyList<CpgNodeDescriptor> _nodeDescriptors;
  private IReadOnlyList<CpgEdgeCandidate> _edgeCandidates;
  private bool _released;

  internal OperationFragmentFacts(
    int order,
    int declarationSpanStart,
    int declarationSpanEnd,
    int bodySpanStart,
    int bodySpanEnd,
    string? owningMethodSymbolKey,
    IReadOnlyList<CpgNodeDescriptor> nodeDescriptors,
    IReadOnlyList<CpgEdgeCandidate> edgeCandidates)
  {
    Order = order;
    DeclarationSpanStart = declarationSpanStart;
    DeclarationSpanEnd = declarationSpanEnd;
    BodySpanStart = bodySpanStart;
    BodySpanEnd = bodySpanEnd;
    OwningMethodSymbolKey = owningMethodSymbolKey;
    _nodeDescriptors = nodeDescriptors;
    _edgeCandidates = edgeCandidates;
  }

  internal int Order { get; }

  internal int DeclarationSpanStart { get; }

  internal int DeclarationSpanEnd { get; }

  internal int BodySpanStart { get; }

  internal int BodySpanEnd { get; }

  internal string? OwningMethodSymbolKey { get; }

  internal IReadOnlyList<CpgNodeDescriptor> NodeDescriptors => _nodeDescriptors;

  internal IReadOnlyList<CpgEdgeCandidate> EdgeCandidates => _edgeCandidates;

  internal bool IsReleased => _released;

  internal void ThrowIfReleased()
  {
    if (_released)
    {
      throw new InvalidOperationException("An operation fragment cannot be committed after it has been released.");
    }
  }

  internal void Release()
  {
    _nodeDescriptors = Array.Empty<CpgNodeDescriptor>();
    _edgeCandidates = Array.Empty<CpgEdgeCandidate>();
    _released = true;
  }
}
