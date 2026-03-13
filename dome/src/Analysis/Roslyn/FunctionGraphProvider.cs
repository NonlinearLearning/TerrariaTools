namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 提供函数调用图快照数据。
/// </summary>
public sealed class FunctionGraphProvider(
    FunctionIndex functionIndex,
    FunctionFactsIndex functionFacts) : IFunctionGraphProvider
{
    /// <summary>
    /// 获取函数图快照。
    /// </summary>
    /// <param name="request">函数图请求。</param>
    /// <returns>函数图快照。</returns>
    public FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request)
    {
        ValidateRequest(request);

        return request.Scope switch
        {
            FunctionGraphScope.WholeProject => BuildWholeProjectSnapshot(),
            FunctionGraphScope.ExpandedMembers => BuildExpandedMembersSnapshot(request.RootMemberIds, request.Depth),
            _ => throw new NotSupportedException($"Unsupported function graph scope '{request.Scope}'.")
        };
    }

    /// <summary>
    /// 构建全项目函数调用图快照。
    /// </summary>
    private FunctionGraphSnapshot BuildWholeProjectSnapshot()
    {
        var nodes = functionFacts.FactsByMemberId.Values
            .Select(fact => fact.Node)
            .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var edges = functionFacts.FactsByMemberId.Values
            .SelectMany(BuildCallEdges)
            .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ToArray();
        var includedDocumentPaths = functionFacts.MemberIdsByDocumentPath.Keys
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new FunctionGraphSnapshot(
            FunctionGraphScope.WholeProject,
            Array.Empty<MemberId>(),
            includedDocumentPaths,
            new FunctionDependencyGraph(nodes, edges));
    }

    /// <summary>
    /// 构建扩展成员函数调用图快照。
    /// </summary>
    private FunctionGraphSnapshot BuildExpandedMembersSnapshot(IReadOnlyList<MemberId> rootMemberIds, int depth)
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
            .Select(memberId => functionIndex.NodesByMemberId.TryGetValue(memberId, out var node) ? node.DocumentPath : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var rebuiltMemberIds = includedDocumentPaths
            .SelectMany(path => functionIndex.MemberIdsByDocumentPath.TryGetValue(path, out var memberIds)
                ? memberIds
                : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var rebuiltMemberIdSet = rebuiltMemberIds.ToHashSet(StringComparer.Ordinal);
        var nodes = rebuiltMemberIds
            .Select(memberId => functionFacts.FactsByMemberId.TryGetValue(memberId, out var fact) ? fact.Node : null)
            .OfType<FunctionNodeRef>()
            .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var edges = functionFacts.FactsByMemberId.Values
            .Where(fact => rebuiltMemberIdSet.Contains(fact.Node.MemberId.Value))
            .SelectMany(BuildCallEdges)
            .Where(edge => rebuiltMemberIdSet.Contains(edge.TargetMemberId.Value))
            .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ToArray();

        return new FunctionGraphSnapshot(
            FunctionGraphScope.ExpandedMembers,
            normalizedRootMemberIds,
            includedDocumentPaths,
            new FunctionDependencyGraph(nodes, edges));
    }

    /// <summary>
    /// 验证函数图请求的有效性。
    /// </summary>
    private static void ValidateRequest(FunctionGraphRequest request)
    {
        if (request.EdgeKinds.Count == 0 ||
            request.EdgeKinds.Any(kind => kind != FunctionDependencyKind.Calls))
        {
            throw new NotSupportedException("Only call edges are supported by the current function graph provider.");
        }

        if (request.Scope == FunctionGraphScope.ExpandedMembers)
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

    /// <summary>
    /// 获取与指定成员有调用关系的相邻成员（调用者和被调用者）。
    /// </summary>
    private IEnumerable<string> GetCallAdjacentMembers(string memberId)
    {
        if (functionFacts.FactsByMemberId.TryGetValue(memberId, out var sourceFact))
        {
            foreach (var calledMemberId in sourceFact.CalledMemberIds)
            {
                yield return calledMemberId.Value;
            }
        }

        if (functionFacts.IncomingCallersByMemberId.TryGetValue(memberId, out var callers))
        {
            foreach (var caller in callers)
            {
                yield return caller.Value;
            }
        }
    }

    /// <summary>
    /// 构建函数调用边。
    /// </summary>
    private static IEnumerable<FunctionDependencyEdge> BuildCallEdges(FunctionFact fact)
    {
        foreach (var calledMemberId in fact.CalledMemberIds)
        {
            yield return new FunctionDependencyEdge(
                fact.Node.MemberId,
                calledMemberId,
                FunctionDependencyKind.Calls);
        }
    }
}
