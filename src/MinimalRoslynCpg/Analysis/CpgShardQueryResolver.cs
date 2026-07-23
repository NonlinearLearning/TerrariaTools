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
  private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
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

  public async Task<IReadOnlyList<CpgFrozenShard>> FindBySymbolAsync(
    string symbolKey,
    CancellationToken cancellationToken)
  {
    _lookupCount += 1;
    var locations = await _catalog.FindBySymbolAsync(new CpgSymbolLookup(symbolKey), cancellationToken);
    return await OpenLocationsAsync(locations, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgFrozenShard>> FindByNodeAsync(
    NodeId nodeId,
    CancellationToken cancellationToken)
  {
    _lookupCount += 1;
    var locations = await _catalog.FindByNodeAsync(nodeId.Value, cancellationToken);
    return await OpenLocationsAsync(locations, cancellationToken);
  }

  public async Task<IReadOnlyList<CpgFrozenShard>> FindBySpanAsync(
    CpgSpanLookup lookup,
    CancellationToken cancellationToken)
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

  private async Task<IReadOnlyList<CpgFrozenShard>> OpenLocationsAsync(
    IReadOnlyList<CpgShardLocation> locations,
    CancellationToken cancellationToken)
  {
    var shards = new List<CpgFrozenShard>(locations.Count);
    foreach (var location in locations.OrderBy(location => location.ShardId, StringComparer.Ordinal))
    {
      if (_entries.Remove(location.ShardId, out var cached))
      {
        _cacheHitCount += 1;
        cached = cached with { LastUsed = Environment.TickCount64 };
        _entries.Add(location.ShardId, cached);
        shards.Add(cached.Shard);
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

    while (_cachedBytes + location.ByteLength > _maxCachedBytes && _entries.Count > 0)
    {
      var oldest = _entries.Values.OrderBy(entry => entry.LastUsed).ThenBy(entry => entry.Location.ShardId, StringComparer.Ordinal).First();
      _entries.Remove(oldest.Location.ShardId);
      _cachedBytes -= oldest.Location.ByteLength;
      _evictionCount += 1;
    }

    _entries[location.ShardId] = new CacheEntry(location, shard, Environment.TickCount64);
    _cachedBytes += location.ByteLength;
  }

  private sealed record CacheEntry(CpgShardLocation Location, CpgFrozenShard Shard, long LastUsed);
}
