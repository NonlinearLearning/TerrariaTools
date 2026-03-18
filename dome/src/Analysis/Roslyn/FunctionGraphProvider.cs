namespace TerrariaTools.Dome.Analysis.Roslyn;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

/// <summary>
/// Provides function graph snapshots for the model-native analysis pipeline.
/// </summary>
public sealed partial class FunctionGraphProvider : ModelAnalysis.IFunctionGraphProvider
{
    private readonly ModelAnalysis.FunctionIndex _functionIndex;
    private readonly ModelAnalysis.FunctionFactsIndex _functionFacts;

    public FunctionGraphProvider(
        ModelAnalysis.FunctionIndex functionIndex,
        ModelAnalysis.FunctionFactsIndex functionFacts)
    {
        _functionIndex = functionIndex;
        _functionFacts = functionFacts;
    }

    public ModelAnalysis.FunctionGraphSnapshot GetSnapshot(ModelAnalysis.FunctionGraphRequest request)
    {
        ValidateRequest(request);

        return request.Scope switch
        {
            ModelAnalysis.FunctionGraphScope.WholeProject => BuildWholeProjectSnapshot(),
            ModelAnalysis.FunctionGraphScope.ExpandedMembers => BuildExpandedMembersSnapshot(request.RootMemberIds, request.Depth),
            _ => throw new NotSupportedException($"Unsupported function graph scope '{request.Scope}'.")
        };
    }

    private ModelAnalysis.FunctionGraphSnapshot BuildWholeProjectSnapshot()
    {
        var nodes = _functionFacts.FactsByMemberId.Values
            .Select(fact => fact.Node)
            .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var edges = _functionFacts.FactsByMemberId.Values
            .SelectMany(BuildCallEdges)
            .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ToArray();
        var includedDocumentPaths = _functionFacts.MemberIdsByDocumentPath.Keys
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new ModelAnalysis.FunctionGraphSnapshot(
            ModelAnalysis.FunctionGraphScope.WholeProject,
            Array.Empty<ModelPrimitives.MemberId>(),
            includedDocumentPaths,
            new ModelAnalysis.FunctionDependencyGraph(nodes, edges));
    }

    private ModelAnalysis.FunctionGraphSnapshot BuildExpandedMembersSnapshot(
        IReadOnlyList<ModelPrimitives.MemberId> rootMemberIds,
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
        var nodes = rebuiltMemberIds
            .Select(memberId => _functionFacts.FactsByMemberId.TryGetValue(memberId, out var fact) ? fact.Node : null)
            .OfType<ModelAnalysis.FunctionNodeRef>()
            .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var edges = _functionFacts.FactsByMemberId.Values
            .Where(fact => rebuiltMemberIdSet.Contains(fact.Node.MemberId.Value))
            .SelectMany(BuildCallEdges)
            .Where(edge => rebuiltMemberIdSet.Contains(edge.TargetMemberId.Value))
            .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ToArray();

        return new ModelAnalysis.FunctionGraphSnapshot(
            ModelAnalysis.FunctionGraphScope.ExpandedMembers,
            normalizedRootMemberIds,
            includedDocumentPaths,
            new ModelAnalysis.FunctionDependencyGraph(nodes, edges));
    }

    private static void ValidateRequest(ModelAnalysis.FunctionGraphRequest request)
    {
        if (request.EdgeKinds.Count == 0 ||
            request.EdgeKinds.Any(kind => kind != ModelPrimitives.FunctionDependencyKind.Calls))
        {
            throw new NotSupportedException("Only call edges are supported by the current function graph provider.");
        }

        if (request.Scope == ModelAnalysis.FunctionGraphScope.ExpandedMembers)
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
        if (_functionFacts.FactsByMemberId.TryGetValue(memberId, out var sourceFact))
        {
            foreach (var calledMemberId in sourceFact.CalledMemberIds)
            {
                yield return calledMemberId.Value;
            }
        }

        if (_functionFacts.IncomingCallersByMemberId.TryGetValue(memberId, out var callers))
        {
            foreach (var caller in callers)
            {
                yield return caller.Value;
            }
        }
    }

    private static IEnumerable<ModelAnalysis.FunctionDependencyEdge> BuildCallEdges(ModelAnalysis.FunctionFact fact)
    {
        foreach (var calledMemberId in fact.CalledMemberIds)
        {
            yield return new ModelAnalysis.FunctionDependencyEdge(
                fact.Node.MemberId,
                calledMemberId,
                ModelPrimitives.FunctionDependencyKind.Calls);
        }
    }
}
