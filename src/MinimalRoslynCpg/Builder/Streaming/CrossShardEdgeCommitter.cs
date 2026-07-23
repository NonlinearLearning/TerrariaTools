using MinimalRoslynCpg.Model;
using MinimalRoslynCpg.Persistence;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Converts one frozen graph edge into a boundary record for fragment-owned adjacency shards.
/// </summary>
internal static class CrossShardEdgeCommitter
{
  internal static CpgFrozenBoundaryEdge Create(RoslynCpgEdge edge)
  {
    ArgumentNullException.ThrowIfNull(edge);
    return new CpgFrozenBoundaryEdge(
      edge.SourceNodeId.Value,
      edge.TargetNodeId.Value,
      edge.Kind.ToString(),
      edge.StructuredLabel?.StableKey,
      edge.ContextId?.Value,
      edge.CallSiteContext?.FilePath,
      edge.CallSiteContext?.SpanStart,
      edge.CallSiteContext?.SpanEnd,
      edge.CallSiteContext?.DisplayName);
  }
}
