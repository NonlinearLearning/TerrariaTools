using NLCPG.Persistence;
using NLCPG.Model;

namespace NLCPG.Analysis;



//查询冻结的CPG分片
public sealed class CpgShardQueryResolver
{
    private readonly ICpgShardCatalog _catalog;
    private readonly ICpgShardStore _store;
    private readonly long _maxCachedBytes;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _recency = new();
    private long _cachedBytes;

    // 绑定目录与存储读取器，并设置分片读取缓存上限。
    public CpgShardQueryResolver(ICpgShardCatalog catalog, ICpgShardStore store, long maxCachedBytes)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _maxCachedBytes = Math.Max(0, maxCachedBytes);
    }

    // 按符号键查询分片位置，并打开命中的冻结分片。
    public async Task<IReadOnlyList<CpgFrozenShard>> FindBySymbolAsync(string symbolKey, CancellationToken cancellationToken)
    {
        var locations = await _catalog.FindBySymbolAsync(new CpgSymbolLookup(symbolKey), cancellationToken);
        return await OpenLocationsAsync(locations, cancellationToken);
    }

    // 按节点标识查询包含该节点的分片，并返回已打开的分片列表。
    public async Task<IReadOnlyList<CpgFrozenShard>> FindByNodeAsync(NodeId nodeId, CancellationToken cancellationToken)
    {
        var locations = await _catalog.FindByNodeAsync(nodeId.Value, cancellationToken);
        return await OpenLocationsAsync(locations, cancellationToken);
    }
    // 按文件跨度查询分片位置，并打开对应的冻结分片。
    public async Task<IReadOnlyList<CpgFrozenShard>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken)
    {
        var locations = await _catalog.FindBySpanAsync(lookup, cancellationToken);
        return await OpenLocationsAsync(locations, cancellationToken);
    }

    private async Task<IReadOnlyList<CpgFrozenShard>> OpenLocationsAsync(IReadOnlyList<CpgShardLocation> locations, CancellationToken cancellationToken)
    {
        var shards = new List<CpgFrozenShard>(locations.Count);
        foreach (var location in locations.OrderBy(location => location.ShardId, StringComparer.Ordinal))
        {
            if (_entries.TryGetValue(location.ShardId, out var cachedNode))
            {
                _recency.Remove(cachedNode);
                _recency.AddLast(cachedNode);
                shards.Add(cachedNode.Value.Shard);
                continue;
            }

            var shard = await _store.ReadAsync(location, cancellationToken);
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
        }

        var entry = new CacheEntry(location, shard);
        _entries[location.ShardId] = _recency.AddLast(entry);
        _cachedBytes += location.ByteLength;
    }

    private sealed record CacheEntry(CpgShardLocation Location, CpgFrozenShard Shard);
}
