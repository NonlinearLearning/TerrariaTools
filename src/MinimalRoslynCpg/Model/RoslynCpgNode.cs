using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 表示最小 Roslyn CPG 中的一个节点。
/// </summary>
public sealed record RoslynCpgNode(
  string Id,
  RoslynCpgNodeKind Kind,
  string DisplayKind,
  string? Name = null,
  string? FullName = null,
  string? Signature = null,
  string? DispatchKind = null,
  string? TypeFullName = null,
  string? FilePath = null,
  int? SpanStart = null,
  int? SpanEnd = null,
  string? Text = null,
  bool IsImplicit = false);
