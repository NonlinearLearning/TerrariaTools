namespace TerrariaTools.Dome.Core.Cpg;

public sealed class DomeCpg
{
    private static readonly IReadOnlyDictionary<string, string> NodeKindsByClrName = SchemaIndex.NodeClrNames
        .ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);

    private readonly List<StoredNode> nodes = new();
    private readonly List<CpgEdge> edges = new();
    private readonly HashSet<string> nodeIds = new(StringComparer.Ordinal);
    private readonly HashSet<CpgEdge> edgeSet = new();
    private readonly Dictionary<string, StoredNode> nodesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StoredNode>> nodesByKind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CpgEdge>> edgesByLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Label, string SourceId), List<CpgEdge>> outgoingEdgesByLabelAndSource = new();
    private readonly Dictionary<(string Label, string TargetId), List<CpgEdge>> incomingEdgesByLabelAndTarget = new();

    public IReadOnlyList<StoredNode> Nodes => nodes;

    public IReadOnlyList<CpgEdge> Edges => edges;

    public MetaDataNode MetaData =>
        nodesByKind.TryGetValue(NodeKinds.MetaData, out List<StoredNode>? metaDataNodes) &&
        metaDataNodes.FirstOrDefault() is MetaDataNode metaDataNode
            ? metaDataNode
            : throw new InvalidOperationException("CPG does not contain a META_DATA node.");

    public RoslynFrontendContext? FrontendContext { get; private set; }

    internal void AddNode(StoredNode node)
    {
        if (nodeIds.Add(node.Id))
        {
            nodes.Add(node);
            nodesById[node.Id] = node;

            if (NodeKindsByClrName.TryGetValue(node.GetType().Name, out string? nodeKind))
            {
                if (!nodesByKind.TryGetValue(nodeKind, out List<StoredNode>? matchedNodes))
                {
                    matchedNodes = new List<StoredNode>();
                    nodesByKind[nodeKind] = matchedNodes;
                }

                matchedNodes.Add(node);
            }
        }
    }

    internal void AddEdge(CpgEdge edge)
    {
        if (edgeSet.Add(edge))
        {
            edges.Add(edge);

            if (!edgesByLabel.TryGetValue(edge.Label, out List<CpgEdge>? matchedEdges))
            {
                matchedEdges = new List<CpgEdge>();
                edgesByLabel[edge.Label] = matchedEdges;
            }

            matchedEdges.Add(edge);

            if (!outgoingEdgesByLabelAndSource.TryGetValue((edge.Label, edge.SourceId), out List<CpgEdge>? outgoingEdges))
            {
                outgoingEdges = new List<CpgEdge>();
                outgoingEdgesByLabelAndSource[(edge.Label, edge.SourceId)] = outgoingEdges;
            }

            outgoingEdges.Add(edge);

            if (!incomingEdgesByLabelAndTarget.TryGetValue((edge.Label, edge.TargetId), out List<CpgEdge>? incomingEdges))
            {
                incomingEdges = new List<CpgEdge>();
                incomingEdgesByLabelAndTarget[(edge.Label, edge.TargetId)] = incomingEdges;
            }

            incomingEdges.Add(edge);
        }
    }

    internal void AttachFrontendContext(RoslynFrontendContext frontendContext)
    {
        FrontendContext = frontendContext;
    }

    public T? FindNodeById<T>(string id)
        where T : StoredNode
    {
        return nodesById.TryGetValue(id, out StoredNode? node) ? node as T : null;
    }

    public bool ContainsNode(string id)
    {
        return nodesById.ContainsKey(id);
    }

    public IReadOnlyList<StoredNode> GetNodesByKind(string nodeKind)
    {
        return nodesByKind.TryGetValue(nodeKind, out List<StoredNode>? matchedNodes)
            ? matchedNodes
            : Array.Empty<StoredNode>();
    }

    public IReadOnlyList<TNode> GetNodesByKind<TNode>(string nodeKind)
        where TNode : StoredNode
    {
        return nodesByKind.TryGetValue(nodeKind, out List<StoredNode>? matchedNodes)
            ? matchedNodes.OfType<TNode>().ToArray()
            : Array.Empty<TNode>();
    }

    public IReadOnlyList<CpgEdge> GetEdgesByLabel(string edgeLabel)
    {
        return edgesByLabel.TryGetValue(edgeLabel, out List<CpgEdge>? matchedEdges)
            ? matchedEdges
            : Array.Empty<CpgEdge>();
    }

    public IReadOnlyList<CpgEdge> GetOutgoingEdges(string edgeLabel, string sourceId)
    {
        return outgoingEdgesByLabelAndSource.TryGetValue((edgeLabel, sourceId), out List<CpgEdge>? matchedEdges)
            ? matchedEdges
            : Array.Empty<CpgEdge>();
    }

    public IReadOnlyList<CpgEdge> GetIncomingEdges(string edgeLabel, string targetId)
    {
        return incomingEdgesByLabelAndTarget.TryGetValue((edgeLabel, targetId), out List<CpgEdge>? matchedEdges)
            ? matchedEdges
            : Array.Empty<CpgEdge>();
    }
}
