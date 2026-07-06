using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 表示两个图节点之间的一条带类型关系边。
/// </summary>
public sealed record RoslynCpgEdge(
  string SourceId,
  string TargetId,
  RoslynCpgEdgeKind Kind,
  string? Label = null);
