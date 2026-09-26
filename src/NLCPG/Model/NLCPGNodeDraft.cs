using NLCPG.Contracts;

namespace NLCPG.Model;

/// 图物化前的节点文本草稿；草稿不会进入冻结图或查询索引。
public readonly record struct NLCPGNodeDraft(
  NLCPGNodeKind Kind,
  string? Name = null,
  string? FullName = null,
  string? Signature = null,
  NLCPGDispatchKind? DispatchKind = null,
  string? TypeFullName = null,
  string? FilePath = null,
  int? SpanStart = null,
  int? SpanEnd = null,
  bool IsImplicit = false,
  string? StableIdentityText = null);
