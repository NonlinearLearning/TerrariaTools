using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Stable information retained after a method fragment has released Roslyn facts.
/// </summary>
internal sealed record CrossShardSummary(
  int SourceOrder,
  StableNodeAnchor SourceBoundaryAnchor,
  StableNodeAnchor CallSiteAnchor,
  string TargetSymbolKey,
  string DispatchKind,
  int CallSiteSpanStart,
  int CallSiteSpanEnd);
