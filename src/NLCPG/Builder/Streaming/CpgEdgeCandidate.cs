using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Streaming;

/// 端点仅在全局分配后解析的不可变边事实。
public readonly record struct CpgEdgeCandidate(
  StableNodeAnchor SourceAnchor,
  StableNodeAnchor TargetAnchor,
  NLCPGEdgeKind Kind,
  NLCPGEdgeLabel? StructuredLabel,
  NLCPGContextId? ContextId,
  NLCPGCallSiteContext? CallSiteContext)
{
  internal NLCPGEdge Materialize(DeterministicNodeIdTable allocation)
  {
    return new NLCPGEdge(
      allocation.GetRequiredId(SourceAnchor),
      allocation.GetRequiredId(TargetAnchor),
      Kind,
      StructuredLabel,
      ContextId,
      CallSiteContext);
  }
}
