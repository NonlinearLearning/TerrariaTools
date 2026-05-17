using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

public sealed class RoslynCpgGraph {
  private readonly Dictionary<string, RoslynCpgNode> _nodes = new(StringComparer.Ordinal);
  private readonly HashSet<RoslynCpgEdge> _edges = new();

  public IReadOnlyCollection<RoslynCpgNode> Nodes => _nodes.Values;

  public IReadOnlyCollection<RoslynCpgEdge> Edges => _edges;

  public RoslynCpgNode AddNode(RoslynCpgNode node) {
    if (!_nodes.TryGetValue(node.Id, out var existing)) {
      _nodes[node.Id] = node;
      return node;
    }

    return existing;
  }

  public void AddEdge(RoslynCpgNode source, RoslynCpgNode target, RoslynCpgEdgeKind kind) {
    AddNode(source);
    AddNode(target);
    _edges.Add(new RoslynCpgEdge(source.Id, target.Id, kind));
  }

  public IEnumerable<RoslynCpgNode> NodesByKind(RoslynCpgNodeKind kind) {
    return _nodes.Values.Where(node => node.Kind == kind);
  }
}
