using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// Performs bounded, deterministic reverse traversal over a frozen CPG index.
/// </summary>
public sealed class RoslynCpgSliceQuery
{
    private readonly RoslynCpgGraph _graph;

    public RoslynCpgSliceQuery(RoslynCpgGraph graph)
    {
        _graph = graph;
    }

    public RoslynCpgSliceResult QueryBackward(string sinkNodeId, RoslynCpgSliceQueryOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkNodeId);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        if (_graph.GetNode(sinkNodeId) is null)
        {
            throw new ArgumentException($"Unknown sink node id: {sinkNodeId}", nameof(sinkNodeId));
        }

        var queue = new Queue<SliceState>();
        queue.Enqueue(new SliceState(sinkNodeId, new[] { sinkNodeId }, options.MaxHops));
        var visitedStates = new HashSet<(string NodeId, int RemainingHops)> { (sinkNodeId, options.MaxHops) };
        var paths = new List<RoslynCpgSlicePath>();
        var visitedNodeIds = new HashSet<string>(StringComparer.Ordinal) { sinkNodeId };
        long visitedEdgeCount = 0;
        string? truncationReason = null;

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            var incoming = _graph.GetIncomingEdges(state.NodeId)
                .Where(edge => options.AllowedEdgeKinds.Contains(edge.Kind))
                .ToArray();
            visitedEdgeCount += incoming.Length;

            if (incoming.Length == 0)
            {
                if (paths.Count >= options.MaxPaths)
                {
                    truncationReason ??= "maxPaths";
                    break;
                }

                AddPath(state, sinkNodeId, paths);
                if (paths.Count >= options.MaxDefinitions)
                {
                    truncationReason ??= "maxDefinitions";
                    break;
                }

                continue;
            }

            if (state.RemainingHops == 0)
            {
                if (paths.Count >= options.MaxPaths)
                {
                    truncationReason ??= "maxPaths";
                    break;
                }

                AddPath(state, sinkNodeId, paths);
                truncationReason ??= "maxHops";
                continue;
            }

            foreach (var edge in incoming)
            {
                if (paths.Count >= options.MaxPaths)
                {
                    truncationReason ??= "maxPaths";
                    break;
                }

                if (state.ReversePath.Contains(edge.SourceId, StringComparer.Ordinal))
                {
                    continue;
                }

                var next = new SliceState(
                    edge.SourceId,
                    state.ReversePath.Append(edge.SourceId).ToArray(),
                    state.RemainingHops - 1);
                if (!visitedStates.Add((next.NodeId, next.RemainingHops)))
                {
                    continue;
                }

                visitedNodeIds.Add(next.NodeId);
                queue.Enqueue(next);
            }

            if (truncationReason == "maxPaths")
            {
                break;
            }
        }

        var orderedPaths = paths
            .DistinctBy(path => string.Join("\u001f", path.NodeIds))
            .OrderBy(path => path.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(path => path.SinkNodeId, StringComparer.Ordinal)
            .ThenBy(path => string.Join("\u001f", path.NodeIds), StringComparer.Ordinal)
            .ToArray();
        return new RoslynCpgSliceResult(
            orderedPaths,
            truncationReason is not null,
            truncationReason,
            visitedNodeIds.Count,
            visitedEdgeCount);
    }

    private static void AddPath(SliceState state, string sinkNodeId, ICollection<RoslynCpgSlicePath> paths)
    {
        var nodeIds = state.ReversePath.Reverse().ToArray();
        paths.Add(new RoslynCpgSlicePath(state.NodeId, sinkNodeId, nodeIds));
    }

    private static void ValidateOptions(RoslynCpgSliceQueryOptions options)
    {
        if (options.AllowedEdgeKinds.Count == 0)
        {
            throw new ArgumentException("At least one edge kind is required.", nameof(options));
        }

        if (options.MaxHops < 0 || options.MaxPaths <= 0 || options.MaxDefinitions <= 0 || options.MaxCallDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Slice budgets must be non-negative, with positive path and definition limits.");
        }
    }

    private sealed record SliceState(string NodeId, IReadOnlyList<string> ReversePath, int RemainingHops);
}
