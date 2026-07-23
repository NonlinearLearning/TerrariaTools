using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Immutable edge fact whose endpoints are resolved only after global allocation.
/// </summary>
internal sealed record CpgEdgeCandidate(
  StableNodeAnchor SourceAnchor,
  StableNodeAnchor TargetAnchor,
  RoslynCpgEdgeKind Kind,
  RoslynCpgEdgeLabel? StructuredLabel,
  RoslynCpgContextId? ContextId,
  RoslynCpgCallSiteContext? CallSiteContext)
{
  internal RoslynCpgEdge Materialize(DeterministicNodeIdTable allocation)
  {
    return new RoslynCpgEdge(
      allocation.GetRequiredId(SourceAnchor),
      allocation.GetRequiredId(TargetAnchor),
      Kind,
      StructuredLabel,
      ContextId,
      CallSiteContext);
  }
}
