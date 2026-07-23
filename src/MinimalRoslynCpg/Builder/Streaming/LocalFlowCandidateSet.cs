namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Immutable, method-local flow edges that can be committed after a fragment has released Roslyn facts.
/// </summary>
internal sealed class LocalFlowCandidateSet
{
  internal LocalFlowCandidateSet(IReadOnlyList<CpgEdgeCandidate> edgeCandidates)
  {
    ArgumentNullException.ThrowIfNull(edgeCandidates);
    EdgeCandidates = edgeCandidates.ToArray();
  }

  internal IReadOnlyList<CpgEdgeCandidate> EdgeCandidates { get; }
}
