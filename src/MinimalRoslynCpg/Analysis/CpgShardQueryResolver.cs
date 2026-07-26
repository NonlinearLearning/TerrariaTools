using MinimalRoslynCpg.Persistence;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

public sealed record CpgShardQueryTelemetry(
  long LookupCount,
  long OpenCount,
  long CacheHitCount,
  long CacheMissCount,
  long BytesRead,
  long EvictionCount);

public sealed class CpgShardQueryResolver
{
  private readonly ICpgShardCatalog _catalog;
  private readonly ICpgShardStore _store;
  private readonly long _maxCachedBytes;
  private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries = new(StringComparer.Ordinal);
  private readonly LinkedList<CacheEntry> _recency = new();
  private long _cachedBytes;
  private long _lookupCount;
  private long _openCount;
  private long _cacheHitCount;
  private long _cacheMissCount;
  private long _bytesRead;
  private long _evictionCount;

  public CpgShardQueryResolver(ICpgShardCatalog catalog, ICpgShardStore store, long maxCachedBytes)
  {
    _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _maxCachedBytes = Math.Max(0, maxCachedBytes);
  }

  public async Task<IReadOnlyList<CpgFrozenShard>> FindBySymbolAsync(string symbolKey, CancellationToken cancellationToken)
  {
    _lookupCount += 1;
    var locations = await _catalog.FindBySymbolAsync(new CpgSymbolLookup(symbolKey), cancellationToken);
    return await OpenLocationsAsync(locations, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgFrozenShard>> FindByNodeAsync(NodeId nodeId, CancellationToken cancellationToken)
  {
    _lookupCount += 1;
    var locations = await _catalog.FindByNodeAsync(nodeId.Value, cancellationToken);
    return await OpenLocationsAsync(locations, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgFrozenShard>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken)
  {
    _lookupCount += 1;
    var locations = await _catalog.FindBySpanAsync(lookup, cancellationToken);
    return await OpenLocationsAsync(locations, cancellationToken);
  }

  public CpgShardQueryTelemetry GetTelemetry()
  {
    return new CpgShardQueryTelemetry(
      _lookupCount, _openCount, _cacheHitCount, _cacheMissCount, _bytesRead, _evictionCount);
  }

  private async Task<IReadOnlyList<CpgFrozenShard>> OpenLocationsAsync(IReadOnlyList<CpgShardLocation> locations, CancellationToken cancellationToken)
  {
    var shards = new List<CpgFrozenShard>(locations.Count);
    foreach (var location in locations.OrderBy(location => location.ShardId, StringComparer.Ordinal))
    {
      if (_entries.TryGetValue(location.ShardId, out var cachedNode))
      {
        _cacheHitCount += 1;
        _recency.Remove(cachedNode);
        _recency.AddLast(cachedNode);
        shards.Add(cachedNode.Value.Shard);
        continue;
      }

      _cacheMissCount += 1;
      _openCount += 1;
      var shard = await _store.ReadAsync(location, cancellationToken);
      _bytesRead += location.ByteLength;
      AddToCache(location, shard);
      shards.Add(shard);
    }

    return shards;
  }

  private void AddToCache(CpgShardLocation location, CpgFrozenShard shard)
  {
    if (_maxCachedBytes == 0 || location.ByteLength > _maxCachedBytes)
    {
      return;
    }

    while (_cachedBytes + location.ByteLength > _maxCachedBytes && _recency.First is not null)
    {
      var oldest = _recency.First;
      _entries.Remove(oldest.Value.Location.ShardId);
      _recency.RemoveFirst();
      _cachedBytes -= oldest.Value.Location.ByteLength;
      _evictionCount += 1;
    }

    var entry = new CacheEntry(location, shard);
    _entries[location.ShardId] = _recency.AddLast(entry);
    _cachedBytes += location.ByteLength;
  }

  private sealed record CacheEntry(CpgShardLocation Location, CpgFrozenShard Shard);
}
