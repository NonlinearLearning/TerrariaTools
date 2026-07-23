namespace MinimalRoslynCpg.Persistence;

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

public sealed record CpgShardLookup(
  CpgFileKey File,
  CpgFragmentKey Fragment,
  int SchemaVersion,
  string ProfileHash);

public sealed record CpgShardLocation(
  string ShardId,
  string ShardPath,
  string ShardHash,
  long ByteLength,
  CpgShardStatus Status);

public sealed record CpgShardLease(CpgShardLookup Lookup, CpgShardLocation Location);

public sealed record CpgReusableFragmentKey(
  string ProjectId,
  string RelativePath,
  int SchemaVersion,
  string ProfileHash,
  string FragmentKind,
  int SpanStart,
  int SpanLength,
  string FragmentHash,
  string NodeIdFingerprint)
{
  public static CpgReusableFragmentKey Create(CpgFrozenShard shard)
  {
    ArgumentNullException.ThrowIfNull(shard);
    var nodeIds = shard.Nodes
      .OrderBy(node => node.NodeId)
      .Select(node => node.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
      System.Text.Encoding.UTF8.GetBytes(string.Join(",", nodeIds)))).ToLowerInvariant();
    return new CpgReusableFragmentKey(
      shard.Lookup.File.ProjectId,
      shard.Lookup.File.RelativePath,
      shard.Lookup.SchemaVersion,
      shard.Lookup.ProfileHash,
      shard.Lookup.Fragment.Kind,
      shard.Lookup.Fragment.SpanStart,
      shard.Lookup.Fragment.SpanLength,
      shard.Lookup.Fragment.FragmentHash,
      fingerprint);
  }
}

public sealed record CpgReusableShardLease(
  CpgReusableFragmentKey Key,
  CpgShardLocation Location,
  string SourceBuildId);

public sealed record CpgCatalogMaintenanceResult(
  int PrunedBuildCount,
  int CandidateShardCount,
  int DeletedShardCount);

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
  string? CallSiteDisplayName = null);

/// <summary>
/// A cross-shard edge whose endpoints are addressed by global NodeId values.
/// </summary>
public sealed record CpgFrozenBoundaryEdge(
  uint SourceNodeId,
  uint TargetNodeId,
  string Kind,
  string? Label,
  string? ContextId,
  string? CallSiteFilePath = null,
  int? CallSiteSpanStart = null,
  int? CallSiteSpanEnd = null,
  string? CallSiteDisplayName = null);

public sealed record CpgBoundaryAdjacency(
  string OwnerFragmentId,
  CpgBoundaryAdjacencyDirection Direction);

public sealed record CpgSymbolLocation(string SymbolKey, int LocalIndex);

public sealed record CpgFrozenShard(
  CpgShardLookup Lookup,
  IReadOnlyList<CpgFrozenNode> Nodes,
  IReadOnlyList<CpgFrozenEdge> Edges,
  IReadOnlyList<CpgSymbolLocation> SymbolLocations,
  IReadOnlyList<CpgFrozenBoundaryEdge>? BoundaryEdges = null,
  CpgShardRole Role = CpgShardRole.Primary,
  CpgBoundaryAdjacency? BoundaryAdjacency = null);

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

public sealed record CpgShardWriteTelemetry(
  long SerializationMilliseconds,
  long ValidationMilliseconds,
  long FlushMilliseconds,
  long ReadBackMilliseconds = 0,
  long HashMilliseconds = 0,
  long StructuralValidationMilliseconds = 0);

public sealed record CpgShardWriteResult(
  CpgShardLocation Location,
  CpgShardWriteTelemetry Telemetry);

public interface ICpgShardCatalog
{
  Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken);

  Task<CpgReusableShardLease?> TryAcquireReusableAsync(
    CpgReusableFragmentKey key,
    CancellationToken cancellationToken);

  Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(
    CpgFileKey fileKey,
    int schemaVersion,
    string profileHash,
    CancellationToken cancellationToken);

  Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(
    uint nodeId,
    CancellationToken cancellationToken);

  Task<IReadOnlyList<CpgShardLocation>> FindBySymbolAsync(
    CpgSymbolLookup lookup,
    CancellationToken cancellationToken);

  Task<IReadOnlyList<CpgShardLocation>> FindBySpanAsync(
    CpgSpanLookup lookup,
    CancellationToken cancellationToken);

  Task PublishAsync(CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken);

  Task<string> BeginBuildAsync(CancellationToken cancellationToken);

  Task StageAsync(
    string buildId,
    CpgShardLease lease,
    CpgFrozenShard shard,
    CancellationToken cancellationToken);

  Task StageReusableAsync(
    string buildId,
    CpgShardLease lease,
    CpgFrozenShard shard,
    CpgReusableFragmentKey reusableKey,
    CancellationToken cancellationToken);

  Task CompleteBuildAsync(string buildId, CancellationToken cancellationToken);

  Task InvalidateBuildAsync(string buildId, CancellationToken cancellationToken);

  Task InvalidateFileAsync(CpgFileKey fileKey, CancellationToken cancellationToken);
}

public interface ICpgShardStore
{
  Task<CpgShardWriteResult> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken);

  Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken);

  Task<CpgFrozenShard?> TryReadAsync(
    CpgShardLocation location,
    CpgShardLookup lookup,
    CancellationToken cancellationToken);

  Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(
    string shardPath,
    CancellationToken cancellationToken);
}

public interface ICpgShardReader
{
  ValueTask<CpgFrozenShard> OpenAsync(CpgShardLease lease, CancellationToken cancellationToken);
}
