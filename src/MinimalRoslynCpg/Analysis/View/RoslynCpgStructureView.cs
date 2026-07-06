using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 基于现有 CPG schema 表达的局部结构视图。
/// </summary>
public sealed record RoslynCpgStructureView(
  /// <summary>
  /// 这份结构视图的根节点。
  /// </summary>
  RoslynCpgNode Root,
  /// <summary>
  /// 结构视图包含的全部节点。
  /// </summary>
  IReadOnlyList<RoslynCpgNode> Nodes,
  /// <summary>
  /// 结构视图包含的全部边。
  /// </summary>
  IReadOnlyList<RoslynCpgEdge> Edges);
