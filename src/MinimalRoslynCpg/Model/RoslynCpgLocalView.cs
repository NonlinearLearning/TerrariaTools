namespace MinimalRoslynCpg.Model;

public sealed record RoslynCpgLocalView(
  RoslynCpgNode Anchor,
  int Hops,
  IReadOnlyCollection<RoslynCpgNode> Nodes,
  IReadOnlyCollection<RoslynCpgEdge> Edges);
