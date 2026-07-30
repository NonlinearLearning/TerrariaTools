using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Persistence;

public enum CpgShardStatus
{
    Building,
    Complete,
    Invalid,
}

public enum CpgShardRole
{
    Primary,
    BoundaryAdjacency,
}

public enum CpgBoundaryAdjacencyDirection
{
    Outgoing,
    Incoming,
}

public sealed record CpgFileKey(string ProjectId, string RelativePath, string SourceHash);

public sealed record CpgFragmentKey(string Kind, int SpanStart, int SpanLength, string FragmentHash);

public sealed record CpgShardLookup(CpgFileKey File, CpgFragmentKey Fragment, int SchemaVersion, string ProfileHash);

public sealed record CpgShardLocation(string ShardId, string ShardPath, string ShardHash, long ByteLength, CpgShardStatus Status);

public sealed record CpgShardLease(CpgShardLookup Lookup, CpgShardLocation Location);

public sealed record CpgBuildRoutingIndexManifest(string RelativePath, int FormatVersion, long ByteLength, string PayloadHash);

public sealed class CpgReusableFragmentKey : IEquatable<CpgReusableFragmentKey>
{
    public required string ProjectId { get; init; }

    public required string RelativePath { get; init; }

    public required int SchemaVersion { get; init; }

    public required string ProfileHash { get; init; }

    public required string FragmentKind { get; init; }

    public required int SpanStart { get; init; }

    public required int SpanLength { get; init; }

    public required string FragmentHash { get; init; }

    public required string NodeIdFingerprint { get; init; }

    // 从已冻结分片提取可复用片段键。
    public static CpgReusableFragmentKey Create(CpgFrozenShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var nodeIds = shard.Nodes
          .OrderBy(node => node.NodeId)
          .Select(node => node.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
          System.Text.Encoding.UTF8.GetBytes(string.Join(",", nodeIds)))).ToLowerInvariant();
        return new CpgReusableFragmentKey
        {
            ProjectId = shard.Lookup.File.ProjectId,
            RelativePath = shard.Lookup.File.RelativePath,
            SchemaVersion = shard.Lookup.SchemaVersion,
            ProfileHash = shard.Lookup.ProfileHash,
            FragmentKind = shard.Lookup.Fragment.Kind,
            SpanStart = shard.Lookup.Fragment.SpanStart,
            SpanLength = shard.Lookup.Fragment.SpanLength,
            FragmentHash = shard.Lookup.Fragment.FragmentHash,
            NodeIdFingerprint = fingerprint,
        };
    }

    public bool Equals(CpgReusableFragmentKey? other)
    {
        return other is not null &&
               string.Equals(ProjectId, other.ProjectId, StringComparison.Ordinal) &&
               string.Equals(RelativePath, other.RelativePath, StringComparison.Ordinal) &&
               SchemaVersion == other.SchemaVersion &&
               string.Equals(ProfileHash, other.ProfileHash, StringComparison.Ordinal) &&
               string.Equals(FragmentKind, other.FragmentKind, StringComparison.Ordinal) &&
               SpanStart == other.SpanStart &&
               SpanLength == other.SpanLength &&
               string.Equals(FragmentHash, other.FragmentHash, StringComparison.Ordinal) &&
               string.Equals(NodeIdFingerprint, other.NodeIdFingerprint, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CpgReusableFragmentKey);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectId, StringComparer.Ordinal);
        hash.Add(RelativePath, StringComparer.Ordinal);
        hash.Add(SchemaVersion);
        hash.Add(ProfileHash, StringComparer.Ordinal);
        hash.Add(FragmentKind, StringComparer.Ordinal);
        hash.Add(SpanStart);
        hash.Add(SpanLength);
        hash.Add(FragmentHash, StringComparer.Ordinal);
        hash.Add(NodeIdFingerprint, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public sealed record CpgReusableShardLease(CpgReusableFragmentKey Key, CpgShardLocation Location, string SourceBuildId);

public sealed record CpgCatalogMaintenanceResult(int PrunedBuildCount, int CandidateShardCount, int DeletedShardCount);

public sealed record CpgSymbolLookup(string SymbolKey);

public sealed record CpgSpanLookup(CpgFileKey File, int SpanStart, int SpanLength);

public sealed record CpgFrozenNode(
  int LocalIndex,
  uint NodeId,
  string Kind,
  string? FilePath,
  int? SpanStart,
  int? SpanEnd,
  string DisplayKind,
  string? Name,
  string? FullName,
  string? Signature,
  bool IsImplicit,
  uint StableFilePathId = 0,
  int StableSpanStart = -1,
  int StableSpanEnd = -1,
  int StableRole = 0,
  int StableOrdinal = 0,
  uint StableExtraKeyId = 0);

public sealed record CpgFrozenEdge(
  int SourceLocalIndex,
  int TargetLocalIndex,
  string Kind,
  string? Label,
  string? ContextId,
  string? CallSiteFilePath = null,
  int? CallSiteSpanStart = null,
  int? CallSiteSpanEnd = null,
  string? CallSiteDisplayName = null,
  CpgFrozenFlowSummaryLabel? FlowSummaryLabel = null);

/// 端点由全局 NodeId 值定位的一条跨分片边。
public sealed record CpgFrozenBoundaryEdge(
  uint SourceNodeId,
  uint TargetNodeId,
  string Kind,
  string? Label,
  string? ContextId,
  string? CallSiteFilePath = null,
  int? CallSiteSpanStart = null,
  int? CallSiteSpanEnd = null,
  string? CallSiteDisplayName = null,
  CpgFrozenFlowSummaryLabel? FlowSummaryLabel = null);

/// <summary>
/// Persists the typed portion of a summary bridge label without requiring stable-key parsing on recovery.
/// </summary>
public sealed record CpgFrozenFlowSummaryLabel(
  NLCPGInterproceduralBridgeKind BridgeKind,
  FlowSummaryResolution Resolution,
  string MethodKey,
  FlowSummaryEndpoint Source,
  FlowSummaryEndpoint Target)
{
    public static CpgFrozenFlowSummaryLabel? From(NLCPGEdgeLabel? label)
    {
        if (label?.InterproceduralBridgeKind is not { } bridgeKind ||
            label.FlowSummaryResolution is not { } resolution ||
            string.IsNullOrWhiteSpace(label.FlowSummaryMethodKey) ||
            label.FlowSummarySource is null ||
            label.FlowSummaryTarget is null)
        {
            return null;
        }

        return new CpgFrozenFlowSummaryLabel(
          bridgeKind,
          resolution,
          label.FlowSummaryMethodKey,
          label.FlowSummarySource,
          label.FlowSummaryTarget);
    }

    public NLCPGEdgeLabel ToEdgeLabel()
    {
        return NLCPGEdgeLabel.ForFlowSummaryBridge(
          BridgeKind,
          Resolution,
          MethodKey,
          Source,
          Target);
    }
}

public sealed record CpgBoundaryAdjacency(string OwnerFragmentId, CpgBoundaryAdjacencyDirection Direction);

public sealed record CpgSymbolLocation(string SymbolKey, int LocalIndex);

public sealed record CpgFrozenShard(
  CpgShardLookup Lookup,
  IReadOnlyList<CpgFrozenNode> Nodes,
  IReadOnlyList<CpgFrozenEdge> Edges,
  IReadOnlyList<CpgSymbolLocation> SymbolLocations,
  IReadOnlyList<CpgFrozenBoundaryEdge>? BoundaryEdges = null,
  CpgShardRole Role = CpgShardRole.Primary,
  CpgBoundaryAdjacency? BoundaryAdjacency = null,
  IReadOnlyList<int>? IncomingEdgeOffsets = null,
  IReadOnlyList<int>? IncomingEdgeIndexes = null);

internal static class CpgFrozenShardIncomingEdgeIndex
{
    internal static (IReadOnlyList<int> Offsets, IReadOnlyList<int> EdgeIndexes) Resolve(CpgFrozenShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        if (TryGet(shard, out var offsets, out var edgeIndexes))
        {
            return (offsets, edgeIndexes);
        }

        var built = Build(shard.Nodes, shard.Edges);
        return (built.Offsets, built.EdgeIndexes);
    }

    internal static bool TryGet(CpgFrozenShard shard, out IReadOnlyList<int> offsets, out IReadOnlyList<int> edgeIndexes)
    {
        ArgumentNullException.ThrowIfNull(shard);
        offsets = shard.IncomingEdgeOffsets ?? Array.Empty<int>();
        edgeIndexes = shard.IncomingEdgeIndexes ?? Array.Empty<int>();
        if (shard.IncomingEdgeOffsets is null || shard.IncomingEdgeIndexes is null)
        {
            return false;
        }

        return offsets.Count == shard.Nodes.Count + 1 && edgeIndexes.Count == shard.Edges.Count;
    }

    internal static (int[] Offsets, int[] EdgeIndexes) Build(IReadOnlyList<CpgFrozenNode> nodes, IReadOnlyList<CpgFrozenEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        var offsets = new int[nodes.Count + 1];
        foreach (var edge in edges)
        {
            ValidateLocalIndex(edge.TargetLocalIndex, nodes.Count, "target");
            offsets[edge.TargetLocalIndex + 1] += 1;
        }

        for (var index = 1; index < offsets.Length; index += 1)
        {
            offsets[index] += offsets[index - 1];
        }

        var next = offsets[..^1].ToArray();
        var edgeIndexes = new int[edges.Count];
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex += 1)
        {
            var targetLocalIndex = edges[edgeIndex].TargetLocalIndex;
            edgeIndexes[next[targetLocalIndex]] = edgeIndex;
            next[targetLocalIndex] += 1;
        }

        return (offsets, edgeIndexes);
    }

    internal static void Validate(IReadOnlyList<CpgFrozenNode> nodes, IReadOnlyList<CpgFrozenEdge> edges, IReadOnlyList<int> offsets, IReadOnlyList<int> edgeIndexes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(edgeIndexes);
        if (offsets.Count != nodes.Count + 1)
        {
            throw new InvalidDataException("The CPG shard incoming-edge offsets are invalid.");
        }

        if (edgeIndexes.Count != edges.Count || offsets[0] != 0 || offsets[^1] != edges.Count)
        {
            throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
        }

        var seen = new bool[edges.Count];
        var previousOffset = 0;
        for (var targetLocalIndex = 0; targetLocalIndex < nodes.Count; targetLocalIndex += 1)
        {
            var nextOffset = offsets[targetLocalIndex + 1];
            if (nextOffset < previousOffset || nextOffset > edges.Count)
            {
                throw new InvalidDataException("The CPG shard incoming-edge offsets are invalid.");
            }

            var previousEdgeIndex = -1;
            for (var position = previousOffset; position < nextOffset; position += 1)
            {
                var edgeIndex = edgeIndexes[position];
                if ((uint)edgeIndex >= (uint)edges.Count || seen[edgeIndex])
                {
                    throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
                }

                var edge = edges[edgeIndex];
                if (edge.TargetLocalIndex != targetLocalIndex || edgeIndex < previousEdgeIndex)
                {
                    throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
                }

                seen[edgeIndex] = true;
                previousEdgeIndex = edgeIndex;
            }

            previousOffset = nextOffset;
        }

        if (seen.Any(used => !used))
        {
            throw new InvalidDataException("The CPG shard incoming-edge index is invalid.");
        }
    }

    private static void ValidateLocalIndex(int localIndex, int nodeCount, string endpoint)
    {
        if ((uint)localIndex >= (uint)nodeCount)
        {
            throw new InvalidDataException(
              $"The CPG shard contains an orphan local {endpoint} edge endpoint.");
        }
    }
}

internal static class CpgFragmentOwnerIdentity
{
    internal static string Create(CpgShardLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return string.Join("|",
          lookup.File.ProjectId,
          lookup.File.RelativePath,
          lookup.File.SourceHash,
          lookup.Fragment.Kind,
          lookup.Fragment.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
          lookup.Fragment.SpanLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
          lookup.Fragment.FragmentHash,
          lookup.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
          lookup.ProfileHash);
    }
}

public interface ICpgShardCatalog
{
    Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken);

    Task<CpgReusableShardLease?> TryAcquireReusableAsync(CpgReusableFragmentKey key, CancellationToken cancellationToken);

    Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(CpgFileKey fileKey, int schemaVersion, string profileHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(uint nodeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CpgShardLocation>> FindBySymbolAsync(CpgSymbolLookup lookup, CancellationToken cancellationToken);

    Task<IReadOnlyList<CpgShardLocation>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken);

    Task PublishAsync(CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken);

    Task<string> BeginBuildAsync(CancellationToken cancellationToken);

    Task StageAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken);

    Task StageReusableAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CpgReusableFragmentKey reusableKey, CancellationToken cancellationToken);

    Task CompleteBuildAsync(string buildId, CancellationToken cancellationToken);

    Task InvalidateBuildAsync(string buildId, CancellationToken cancellationToken);

    Task InvalidateFileAsync(CpgFileKey fileKey, CancellationToken cancellationToken);
}

public interface ICpgShardStore
{
    Task<CpgShardLocation> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken);

    Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken);

    Task<CpgFrozenShard?> TryReadAsync(CpgShardLocation location, CpgShardLookup lookup, CancellationToken cancellationToken);

    Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(string shardPath, CancellationToken cancellationToken);
}

public interface ICpgShardReader
{
    ValueTask<CpgFrozenShard> OpenAsync(CpgShardLease lease, CancellationToken cancellationToken);
}
