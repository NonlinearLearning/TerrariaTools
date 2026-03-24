namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;

public sealed partial class FunctionGraphProvider : CoreAnalysis.IFunctionGraphProvider
{
    private readonly CoreAnalysis.FunctionIndex _functionIndex;
    private readonly CoreAnalysis.FunctionDependencyGraph _functionGraph;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _outgoingCallsByMemberId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _incomingCallsByMemberId;

    public FunctionGraphProvider(
        CoreAnalysis.FunctionIndex functionIndex,
        CoreAnalysis.FunctionDependencyGraph functionGraph)
    {
        _functionIndex = functionIndex;
        _functionGraph = functionGraph;
        _outgoingCallsByMemberId = BuildCallMap(
            functionGraph.Edges,
            static edge => edge.SourceMemberId.Value,
            static edge => edge.TargetMemberId.Value);
        _incomingCallsByMemberId = BuildCallMap(
            functionGraph.Edges,
            static edge => edge.TargetMemberId.Value,
            static edge => edge.SourceMemberId.Value);
    }

    public CoreAnalysis.FunctionGraphSnapshot GetSnapshot(CoreAnalysis.FunctionGraphRequest request)
    {
        ValidateRequest(request);

        return request.Scope switch
        {
            CoreAnalysis.FunctionGraphScope.WholeProject => BuildWholeProjectSnapshot(),
            CoreAnalysis.FunctionGraphScope.ExpandedMembers => BuildExpandedMembersSnapshot(request.RootMemberIds, request.Depth),
            _ => throw new NotSupportedException($"Unsupported function graph scope '{request.Scope}'.")
        };
    }

    private CoreAnalysis.FunctionGraphSnapshot BuildWholeProjectSnapshot()
    {
        var includedDocumentPaths = _functionGraph.Nodes
            .Select(node => node.DocumentPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new CoreAnalysis.FunctionGraphSnapshot(
            CoreAnalysis.FunctionGraphScope.WholeProject,
            Array.Empty<CoreCommon.MemberId>(),
            includedDocumentPaths,
            _functionGraph);
    }

    private CoreAnalysis.FunctionGraphSnapshot BuildExpandedMembersSnapshot(
        IReadOnlyList<CoreCommon.MemberId> rootMemberIds,
        int depth)
    {
        if (depth < 0)
        {
            depth = 0;
        }

        var normalizedRootMemberIds = rootMemberIds
            .DistinctBy(id => id.Value)
            .ToArray();
        var includedMemberIds = new HashSet<string>(normalizedRootMemberIds.Select(id => id.Value), StringComparer.Ordinal);
        var frontier = new HashSet<string>(includedMemberIds, StringComparer.Ordinal);

        for (var currentDepth = 0; currentDepth < depth; currentDepth++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var current in frontier)
            {
                foreach (var adjacent in GetCallAdjacentMembers(current))
                {
                    if (includedMemberIds.Add(adjacent))
                    {
                        next.Add(adjacent);
                    }
                }
            }

            if (next.Count == 0)
            {
                break;
            }

            frontier = next;
        }

        var includedDocumentPaths = includedMemberIds
            .Select(memberId => _functionIndex.NodesByMemberId.TryGetValue(memberId, out var node) ? node.DocumentPath : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var rebuiltMemberIds = includedDocumentPaths
            .SelectMany(path => _functionIndex.MemberIdsByDocumentPath.TryGetValue(path, out var memberIds)
                ? memberIds
                : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var rebuiltMemberIdSet = rebuiltMemberIds.ToHashSet(StringComparer.Ordinal);
        var nodesByMemberId = _functionGraph.Nodes.ToDictionary(node => node.MemberId.Value, StringComparer.Ordinal);
        var nodes = rebuiltMemberIds
            .Select(memberId => nodesByMemberId.TryGetValue(memberId, out var node) ? node : null)
            .OfType<CoreAnalysis.FunctionNodeRef>()
            .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var edges = _functionGraph.Edges
            .Where(edge => rebuiltMemberIdSet.Contains(edge.SourceMemberId.Value))
            .Where(edge => rebuiltMemberIdSet.Contains(edge.TargetMemberId.Value))
            .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ToArray();

        return new CoreAnalysis.FunctionGraphSnapshot(
            CoreAnalysis.FunctionGraphScope.ExpandedMembers,
            normalizedRootMemberIds,
            includedDocumentPaths,
            new CoreAnalysis.FunctionDependencyGraph(nodes, edges));
    }

    private static void ValidateRequest(CoreAnalysis.FunctionGraphRequest request)
    {
        if (request.EdgeKinds.Count == 0 ||
            request.EdgeKinds.Any(kind => kind != CoreCommon.FunctionDependencyKind.Calls))
        {
            throw new NotSupportedException("Only call edges are supported by the current function graph provider.");
        }

        if (request.Scope == CoreAnalysis.FunctionGraphScope.ExpandedMembers)
        {
            if (request.RootMemberIds.Count == 0)
            {
                throw new ArgumentException("ExpandedMembers snapshots require at least one root member id.", nameof(request));
            }

            if (request.Depth != 1)
            {
                throw new NotSupportedException("ExpandedMembers snapshots currently only support depth = 1.");
            }
        }
    }

    private IEnumerable<string> GetCallAdjacentMembers(string memberId)
    {
        if (_outgoingCallsByMemberId.TryGetValue(memberId, out var outgoingCalls))
        {
            foreach (var calledMemberId in outgoingCalls)
            {
                yield return calledMemberId;
            }
        }

        if (_incomingCallsByMemberId.TryGetValue(memberId, out var incomingCalls))
        {
            foreach (var caller in incomingCalls)
            {
                yield return caller;
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildCallMap(
        IReadOnlyList<CoreAnalysis.FunctionDependencyEdge> edges,
        Func<CoreAnalysis.FunctionDependencyEdge, string> keySelector,
        Func<CoreAnalysis.FunctionDependencyEdge, string> valueSelector)
    {
        return edges
            .GroupBy(keySelector, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(valueSelector)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(memberId => memberId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }
}



