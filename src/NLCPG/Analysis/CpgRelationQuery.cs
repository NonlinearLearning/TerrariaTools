using NLCPG.Contracts;
using NLCPG.Model;
using System.Collections.Frozen;

namespace NLCPG.Analysis;

/// Fixed, reviewed relation sets available to rule consumers.
public enum CpgRelationProfile
{
    StructuralContainment,
    SemanticBinding,
    LocalDataFlow,
    ControlDependence,
    BackwardSlice,
}

public enum CpgQueryDirection
{
    Incoming,
    Outgoing,
    Bidirectional,
}

public enum CpgQueryPurpose
{
    StructureView,
    RuleAnalysis,
    Evidence,
}

public enum CpgQueryStatus
{
    Complete,
    Truncated,
    Unavailable,
    Disconnected,
    Ambiguous,
}

/// A closed selector intersection. Callers cannot inject arbitrary node predicates.
public sealed record CpgNodeSelector(
    IReadOnlySet<NLCPGNodeKind>? NodeKinds = null,
    StableNodeRole? Role = null,
    string? FilePath = null,
    int? SpanStart = null,
    int? SpanEnd = null,
    string? SymbolKey = null,
    IReadOnlySet<NodeId>? NodeIds = null)
{
    public bool Matches(NLCPGNode node)
    {
        if (NodeKinds is not null && !NodeKinds.Contains(node.Kind))
        {
            return false;
        }

        if (Role.HasValue && node.StableAnchor?.Role != Role.Value)
        {
            return false;
        }

        if (FilePath is not null && !string.Equals(node.FilePath, FilePath, StringComparison.Ordinal))
        {
            return false;
        }

        if (SpanStart.HasValue && node.SpanStart != SpanStart.Value)
        {
            return false;
        }

        if (SpanEnd.HasValue && node.SpanEnd != SpanEnd.Value)
        {
            return false;
        }

        if (SymbolKey is not null &&
            !string.Equals(node.FullName, SymbolKey, StringComparison.Ordinal) &&
            !string.Equals(node.Name, SymbolKey, StringComparison.Ordinal))
        {
            return false;
        }

        return NodeIds is null || (node.NodeId.HasValue && NodeIds.Contains(node.NodeId.Value));
    }

    internal string CacheKey => string.Join(
        "|",
        string.Join(",", (NodeKinds ?? new HashSet<NLCPGNodeKind>()).OrderBy(kind => kind)),
        Role?.ToString() ?? string.Empty,
        FilePath ?? string.Empty,
        SpanStart?.ToString() ?? string.Empty,
        SpanEnd?.ToString() ?? string.Empty,
        SymbolKey ?? string.Empty,
        string.Join(",", (NodeIds ?? new HashSet<NodeId>()).OrderBy(nodeId => nodeId)));
}

public sealed record CpgRelationQuery(
    CpgRelationProfile Profile,
    CpgQueryDirection Direction,
    CpgNodeSelector Source,
    CpgNodeSelector? Target,
    NLCPGTraversalBudget Budget,
    NLCPGCapability RequiredCapabilities,
    CpgQueryPurpose Purpose = CpgQueryPurpose.RuleAnalysis);

public sealed record CpgRelationPath(
    NodeId SourceNodeId,
    NodeId TargetNodeId,
    IReadOnlyList<NodeId> NodeIds,
    IReadOnlyList<NLCPGEdge> Edges);

public sealed record CpgRelationQueryResult(
    IReadOnlyList<NLCPGNode> Nodes,
    IReadOnlyList<NLCPGEdge> Edges,
    IReadOnlyList<CpgRelationPath> Paths,
    CpgQueryStatus Status,
    string? TruncationReason,
    IReadOnlyList<CpgShardUnavailableResult> UnavailableShards,
    NLCPGCapability UsedCapabilities,
    bool WasCacheHit,
    long VisitedNodeCount,
    long VisitedEdgeCount,
    long LoadedShardBytes,
    CpgRelationQueryMetrics? Metrics = null)
{
    public bool WasTruncated => Status == CpgQueryStatus.Truncated;
}

/// Timing and cache counters reported independently by bounded relation queries.
public sealed record CpgRelationQueryMetrics(
    long QueryExecutionElapsedMilliseconds,
    long PathMaterializationElapsedMilliseconds,
    long ShardLoadElapsedMilliseconds,
    long CacheHitCount);

public interface ICpgRelationQueryService
{
    CpgRelationQueryResult Query(CpgRelationQuery query);

    Task<CpgRelationQueryResult> QueryAsync(CpgRelationQuery query, CancellationToken cancellationToken);
}

/// The single owner of profile-to-edge and profile-to-capability policy.
public static class CpgRelationProfiles
{
    public static IReadOnlySet<NLCPGEdgeKind> GetAllowedEdgeKinds(CpgRelationProfile profile)
    {
        return profile switch
        {
            CpgRelationProfile.StructuralContainment => StructuralContainment,
            CpgRelationProfile.SemanticBinding => SemanticBinding,
            CpgRelationProfile.LocalDataFlow => LocalDataFlow,
            CpgRelationProfile.ControlDependence => ControlDependence,
            CpgRelationProfile.BackwardSlice => BackwardSlice,
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }

    public static NLCPGCapability GetRequiredCapabilities(CpgRelationProfile profile)
    {
        return profile switch
        {
            CpgRelationProfile.StructuralContainment => NLCPGCapability.SyntaxSemantic |
                NLCPGCapability.SyntaxToken,
            CpgRelationProfile.SemanticBinding => NLCPGCapability.SyntaxSemantic |
                NLCPGCapability.Reference |
                NLCPGCapability.TypeRef,
            CpgRelationProfile.LocalDataFlow => NLCPGCapability.DataFlow | NLCPGCapability.Cfg,
            CpgRelationProfile.ControlDependence => NLCPGCapability.ControlDependence,
            CpgRelationProfile.BackwardSlice => NLCPGCapability.DataFlow | NLCPGCapability.QueryIndex,
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }

    public static bool AllowsBidirectional(CpgRelationProfile profile)
    {
        return profile == CpgRelationProfile.StructuralContainment;
    }

    private static readonly IReadOnlySet<NLCPGEdgeKind> StructuralContainment = new[]
    {
        NLCPGEdgeKind.SyntaxChild,
        NLCPGEdgeKind.TokenChild,
        NLCPGEdgeKind.SyntaxHasOperation,
        NLCPGEdgeKind.OpHasSyntax,
    }.ToFrozenSet();

    private static readonly IReadOnlySet<NLCPGEdgeKind> SemanticBinding = new[]
    {
        NLCPGEdgeKind.DeclaresSymbol,
        NLCPGEdgeKind.ReferencesSymbol,
        NLCPGEdgeKind.Ref,
        NLCPGEdgeKind.HasType,
        NLCPGEdgeKind.EvalType,
        NLCPGEdgeKind.RefersToType,
    }.ToFrozenSet();

    private static readonly IReadOnlySet<NLCPGEdgeKind> LocalDataFlow = new[]
    {
        NLCPGEdgeKind.DataFlow,
        NLCPGEdgeKind.CfgNext,
        NLCPGEdgeKind.CfgTrue,
        NLCPGEdgeKind.CfgFalse,
    }.ToFrozenSet();

    private static readonly IReadOnlySet<NLCPGEdgeKind> ControlDependence = new[]
    {
        NLCPGEdgeKind.CfgNext,
        NLCPGEdgeKind.CfgTrue,
        NLCPGEdgeKind.CfgFalse,
        NLCPGEdgeKind.Dominates,
        NLCPGEdgeKind.PostDominates,
        NLCPGEdgeKind.ControlDependence,
    }.ToFrozenSet();

    private static readonly IReadOnlySet<NLCPGEdgeKind> BackwardSlice = new[]
    {
        NLCPGEdgeKind.DataFlow,
        NLCPGEdgeKind.InterproceduralDataFlow,
    }.ToFrozenSet();
}
