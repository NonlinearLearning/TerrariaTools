using MinimalRoslynCpg.Model;
using MinimalRoslynCpg.Persistence;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Freezes one operation fragment into a shard and releases its transient facts.
/// </summary>
internal static class StreamingFragmentCommitter
{
  internal static CpgFrozenShard Commit(
    CpgShardLookup lookup,
    OperationFragmentFacts facts,
    DeterministicNodeIdTable allocation,
    ICollection<CpgFrozenBoundaryEdge> boundaryEdges)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    ArgumentNullException.ThrowIfNull(facts);
    ArgumentNullException.ThrowIfNull(allocation);
    ArgumentNullException.ThrowIfNull(boundaryEdges);
    facts.ThrowIfReleased();
    try
    {
      return CpgFrozenShardExporter.ExportDescriptors(
        lookup,
        facts.NodeDescriptors,
        facts.EdgeCandidates,
        allocation,
        boundaryEdges);
    }
    finally
    {
      facts.Release();
    }
  }
}
