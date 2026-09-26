using NLCPG.Model;
using NLCPG.Persistence;

namespace NLCPG.Builder.Streaming;

/// 将一条冻结图边转换为由片段拥有的邻接分片边界记录。
internal static class CrossShardEdgeCommitter
{
  internal static CpgFrozenBoundaryEdge Create(NLCPGEdge edge)
  {
    return new CpgFrozenBoundaryEdge(
      edge.SourceNodeId.Value,
      edge.TargetNodeId.Value,
      edge.Kind.ToString(),
      edge.StructuredLabel?.StableKey,
      edge.ContextId?.Value,
      edge.CallSiteContext?.FilePath,
      edge.CallSiteContext?.SpanStart,
      edge.CallSiteContext?.SpanEnd,
      edge.CallSiteContext?.DisplayName,
      CpgFrozenFlowSummaryLabel.From(edge.StructuredLabel));
  }
}
