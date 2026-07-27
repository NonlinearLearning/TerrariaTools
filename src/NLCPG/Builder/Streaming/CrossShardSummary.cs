using NLCPG.Model;

namespace NLCPG.Builder.Streaming;

/// 方法片段释放 Roslyn 事实后保留的稳定信息。
internal sealed record CrossShardSummary(
  int SourceOrder,
  StableNodeAnchor SourceBoundaryAnchor,
  StableNodeAnchor CallSiteAnchor,
  string TargetSymbolKey,
  string DispatchKind,
  int CallSiteSpanStart,
  int CallSiteSpanEnd);
