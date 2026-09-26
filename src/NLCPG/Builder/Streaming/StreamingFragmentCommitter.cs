using NLCPG.Model;
using NLCPG.Persistence;

namespace NLCPG.Builder.Streaming;

/// 将一个操作片段冻结为分片，并释放其临时事实。
internal static class StreamingFragmentCommitter
{
  internal static CpgFrozenShard Commit(CpgShardLookup lookup, OperationFragmentFacts facts, DeterministicNodeIdTable allocation, ICollection<CpgFrozenBoundaryEdge> boundaryEdges, StringInterner stringInterner)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    ArgumentNullException.ThrowIfNull(facts);
    ArgumentNullException.ThrowIfNull(allocation);
    ArgumentNullException.ThrowIfNull(boundaryEdges);
    ArgumentNullException.ThrowIfNull(stringInterner);
    facts.ThrowIfReleased();
    try
    {
      return CpgFrozenShardExporter.ExportDescriptors(
        lookup,
        facts.NodeDescriptors,
        facts.EdgeCandidates,
        allocation,
        boundaryEdges,
        stringInterner);
    }
    finally
    {
      facts.Release();
    }
  }
}
