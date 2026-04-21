using Domain.Analysis;
using Domain.Analysis.Engine.Core;

namespace Logic.Analysis;

/// <summary>
/// 负责将分析图组装为领域 CPG 快照。
/// </summary>
public sealed class AnalysisCpgSnapshotAssembler : IAnalysisCpgSnapshotAssembler
{

    public AnalysisCpgSnapshot Assemble(
        CpgGraph graph,
        AnalysisInputDescriptor inputDescriptor,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(inputDescriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrySymbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(
            inputDescriptor.WorkspaceContextId,
            minimumTarget,
            entrySymbol,
            depth);

        IReadOnlyCollection<CpgNode> selectedNodes = SelectNodes(graph, entrySymbol, depth);
        foreach (CpgNode node in selectedNodes)
        {
            snapshot.AddNode(MapNode(node));
        }

        HashSet<string> selectedNodeIds = selectedNodes
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (CpgEdge edge in graph.Edges)
        {
            string sourceId = edge.SourceId.ToString();
            string targetId = edge.TargetId.ToString();
            if (!selectedNodeIds.Contains(sourceId) || !selectedNodeIds.Contains(targetId))
            {
                continue;
            }

            if (edge.Kind == CpgEdgeKind.Call || edge.Kind == CpgEdgeKind.MethodRef)
            {
                CpgNode targetNode = graph.GetNode(edge.TargetId);
                snapshot.AddCall(new CpgCall(
                    sourceId,
                    targetId,
                    MapCallKind(targetNode),
                    ReadDisplayName(targetNode)));
            }

            CpgFlowKind? flowKind = MapFlowKind(edge.Kind);
            if (flowKind is not null)
            {
                snapshot.AddFlow(new CpgFlow(sourceId, targetId, flowKind.Value));
            }
        }

        return snapshot;
    }

    private static IReadOnlyCollection<CpgNode> SelectNodes(CpgGraph graph, string entrySymbol, int depth)
    {
        CpgNode? entryNode = graph.Nodes.FirstOrDefault(node => MatchesEntry(node, entrySymbol));
        List<CpgNode> selected = new();

        if (entryNode is not null)
        {
            selected.Add(entryNode);
            selected.AddRange(ExpandFromEntry(graph, entryNode, depth));
        }
        else
        {
            selected.AddRange(graph.Nodes
                .Where(node => IsDomainVisibleNode(node.Kind))
                .Take(Math.Max(depth * 8, depth))
                .ToArray());
        }

        return selected
            .GroupBy(node => node.Id)
            .Select(group => group.First())
            .OrderBy(node => node.Id)
            .ToArray();
    }

    private static IEnumerable<CpgNode> ExpandFromEntry(CpgGraph graph, CpgNode entryNode, int depth)
    {
        Queue<(long NodeId, int Distance)> queue = new();
        HashSet<long> visited = new() { entryNode.Id };
        queue.Enqueue((entryNode.Id, 0));

        while (queue.Count > 0)
        {
            (long nodeId, int distance) = queue.Dequeue();
            if (distance >= depth)
            {
                continue;
            }

            foreach (CpgEdge edge in graph.GetOutgoingEdges(nodeId).Concat(graph.GetIncomingEdges(nodeId)))
            {
                long nextNodeId = edge.SourceId == nodeId ? edge.TargetId : edge.SourceId;
                if (!visited.Add(nextNodeId))
                {
                    continue;
                }

                CpgNode nextNode = graph.GetNode(nextNodeId);
                if (IsDomainVisibleNode(nextNode.Kind))
                {
                    yield return nextNode;
                }

                queue.Enqueue((nextNodeId, distance + 1));
            }
        }
    }

    private static bool MatchesEntry(CpgNode node, string entrySymbol)
    {
        string displayName = ReadDisplayName(node);
        return string.Equals(displayName, entrySymbol, StringComparison.Ordinal)
            || displayName.EndsWith(entrySymbol, StringComparison.Ordinal)
            || entrySymbol.EndsWith(displayName, StringComparison.Ordinal);
    }

    private static MinimumNode MapNode(CpgNode node)
    {
        string documentPath = ReadString(node, "FileName")
            ?? ReadString(node, "FullName")
            ?? "analysis://unknown";
        int line = ReadInt(node, "Line") ?? 1;
        int column = ReadInt(node, "Column") ?? 1;
        string displayName = ReadDisplayName(node);

        return new MinimumNode(
            node.Id.ToString(),
            displayName,
            MapNodeType(node.Kind),
            new LocationRange(
                documentPath,
                Math.Max(line, 1),
                Math.Max(column, 1),
                Math.Max(line, 1),
                Math.Max(column + displayName.Length, 1)));
    }

    private static CpgType MapNodeType(CpgNodeKind nodeKind)
    {
        return nodeKind switch
        {
            CpgNodeKind.File => CpgType.File,
            CpgNodeKind.TypeDecl or CpgNodeKind.Type => CpgType.TypeDecl,
            CpgNodeKind.Method => CpgType.Method,
            CpgNodeKind.Call or CpgNodeKind.MethodRef => CpgType.Call,
            _ => CpgType.Unknown,
        };
    }

    private static CpgCallKind MapCallKind(CpgNode targetNode)
    {
        string dispatchType = ReadString(targetNode, "DispatchType") ?? string.Empty;
        if (dispatchType.Contains("dynamic", StringComparison.OrdinalIgnoreCase))
        {
            return CpgCallKind.Dynamic;
        }

        if (dispatchType.Contains("static", StringComparison.OrdinalIgnoreCase))
        {
            return CpgCallKind.Static;
        }

        return CpgCallKind.Instance;
    }

    private static CpgFlowKind? MapFlowKind(CpgEdgeKind edgeKind)
    {
        return edgeKind switch
        {
            CpgEdgeKind.Cfg or CpgEdgeKind.Ast or CpgEdgeKind.Contains => CpgFlowKind.Sequential,
            CpgEdgeKind.Cdg or CpgEdgeKind.Condition => CpgFlowKind.Conditional,
            CpgEdgeKind.ReachingDef => CpgFlowKind.Return,
            _ => null,
        };
    }

    private static bool IsDomainVisibleNode(CpgNodeKind nodeKind)
    {
        return nodeKind is CpgNodeKind.File
            or CpgNodeKind.TypeDecl
            or CpgNodeKind.Type
            or CpgNodeKind.Method
            or CpgNodeKind.Call
            or CpgNodeKind.MethodRef;
    }

    private static string ReadDisplayName(CpgNode node)
    {
        return ReadString(node, "FullName")
            ?? ReadString(node, "MethodFullName")
            ?? ReadString(node, "Name")
            ?? ReadString(node, "Code")
            ?? $"{node.Kind}:{node.Id}";
    }

    private static string? ReadString(CpgNode node, string propertyName)
    {
        return node.TryGetProperty(propertyName, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static int? ReadInt(CpgNode node, string propertyName)
    {
        if (node.TryGetProperty(propertyName, out int value))
        {
            return value;
        }

        return node.TryGetProperty(propertyName, out long longValue) ? checked((int)longValue) : null;
    }
}
