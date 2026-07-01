using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 基于现有 CPG schema 表达的局部结构视图。
/// </summary>
public sealed record RoslynCpgStructureView(
  RoslynCpgNode Root,
  IReadOnlyList<RoslynCpgNode> Nodes,
  IReadOnlyList<RoslynCpgEdge> Edges);
