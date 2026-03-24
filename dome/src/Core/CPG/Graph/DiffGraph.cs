namespace TerrariaTools.Dome.Core.Cpg;

public sealed class DiffGraph
{
    private readonly List<StoredNode> nodes = new();
    private readonly List<CpgEdge> edges = new();

    public IReadOnlyList<StoredNode> Nodes => nodes;

    public IReadOnlyList<CpgEdge> Edges => edges;

    public void AddNode(StoredNode node)
    {
        nodes.Add(node);
    }

    public void AddEdge(CpgEdge edge)
    {
        edges.Add(edge);
    }
}
