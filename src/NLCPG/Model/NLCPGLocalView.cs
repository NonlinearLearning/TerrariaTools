namespace NLCPG.Model;

/// 保存围绕单个锚点提取出的 hop 受限局部子图。
public sealed record NLCPGLocalView(
  NLCPGNode Anchor,
  int Hops,
  IReadOnlyCollection<NLCPGNode> Nodes,
  IReadOnlyCollection<NLCPGEdge> Edges);
