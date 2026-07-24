namespace MinimalRoslynCpg.Persistence;

public sealed record CpgBuildRoutingPrimaryNodeRoute(uint NodeId, string ShardId, int LocalOffset);

public sealed record CpgBuildRoutingBoundaryNodeRoute(uint NodeId, string ShardId);

public sealed record CpgBuildRoutingSpanRoute(
  CpgFileKey File,
  int SpanStart,
  int SpanLength,
  string ShardId);

public sealed record CpgBuildRoutingSymbolRoute(
  string SymbolKey,
  string ShardId,
  int LocalOffset);

public sealed record CpgBuildRoutingShardEntry(CpgFrozenShard Shard, CpgShardLocation Location);

public sealed record CpgBuildRoutingIndexWriteResult(
  string IndexPath,
  int FormatVersion,
  long ByteLength,
  string PayloadHash);

public sealed class CpgBuildRoutingIndex
{
  private readonly IReadOnlyList<CpgBuildRoutingPrimaryNodeRoute> _primaryNodes;
  private readonly IReadOnlyList<CpgBuildRoutingBoundaryNodeRoute> _boundaryNodes;
  private readonly IReadOnlyList<CpgBuildRoutingSpanRoute> _spans;
  private readonly IReadOnlyList<CpgBuildRoutingSymbolRoute> _symbols;

  internal CpgBuildRoutingIndex(
    string buildId,
    int schemaVersion,
    string profileHash,
    string payloadHash,
    IReadOnlyList<CpgBuildRoutingPrimaryNodeRoute> primaryNodes,
    IReadOnlyList<CpgBuildRoutingBoundaryNodeRoute> boundaryNodes,
    IReadOnlyList<CpgBuildRoutingSpanRoute> spans,
    IReadOnlyList<CpgBuildRoutingSymbolRoute> symbols)
  {
    BuildId = buildId;
    SchemaVersion = schemaVersion;
    ProfileHash = profileHash;
    PayloadHash = payloadHash;
    _primaryNodes = primaryNodes;
    _boundaryNodes = boundaryNodes;
    _spans = spans;
    _symbols = symbols;
  }

  public string BuildId { get; }

  public int SchemaVersion { get; }

  public string ProfileHash { get; }

  public string PayloadHash { get; }

  public IReadOnlyList<CpgBuildRoutingPrimaryNodeRoute> FindPrimaryNode(uint nodeId)
  {
    return FindRange(_primaryNodes, nodeId, static route => route.NodeId);
  }

  public IReadOnlyList<CpgBuildRoutingBoundaryNodeRoute> FindBoundaryNode(uint nodeId)
  {
    return FindRange(_boundaryNodes, nodeId, static route => route.NodeId);
  }

  public IReadOnlyList<CpgBuildRoutingSpanRoute> FindBySpan(CpgSpanLookup lookup)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    return _spans.Where(route =>
        route.File == lookup.File &&
        route.SpanStart == lookup.SpanStart &&
        route.SpanLength == lookup.SpanLength)
      .ToArray();
  }

  public IReadOnlyList<CpgBuildRoutingSymbolRoute> FindBySymbol(CpgSymbolLookup lookup)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    return _symbols.Where(route => string.Equals(route.SymbolKey, lookup.SymbolKey, StringComparison.Ordinal))
      .ToArray();
  }

  private static IReadOnlyList<T> FindRange<T>(
    IReadOnlyList<T> routes,
    uint nodeId,
    Func<T, uint> getNodeId)
  {
    var first = 0;
    var last = routes.Count - 1;
    while (first <= last)
    {
      var middle = first + ((last - first) / 2);
      if (getNodeId(routes[middle]) < nodeId)
      {
        first = middle + 1;
      }
      else
      {
        last = middle - 1;
      }
    }

    var results = new List<T>();
    while (first < routes.Count && getNodeId(routes[first]) == nodeId)
    {
      results.Add(routes[first]);
      first += 1;
    }

    return results;
  }
}
