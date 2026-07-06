namespace MinimalRoslynCpg.Model;

/// <summary>
/// 保存围绕单个锚点提取出的 hop 受限局部子图。
/// </summary>
public sealed record RoslynCpgLocalView(
  RoslynCpgNode Anchor,
  int Hops,
  IReadOnlyCollection<RoslynCpgNode> Nodes,
  IReadOnlyCollection<RoslynCpgEdge> Edges);
