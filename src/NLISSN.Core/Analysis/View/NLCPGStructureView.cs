using NLCPG.Model;

namespace NLISSN.Core.Analysis;

/// 基于现有 CPG schema 表达的局部结构视图。
public sealed record NLCPGStructureView(
  /// 这份结构视图的根节点。
  NLCPGNode Root,
  /// 结构视图包含的全部节点。
  IReadOnlyList<NLCPGNode> Nodes,
  /// 结构视图包含的全部边。
  IReadOnlyList<NLCPGEdge> Edges);
