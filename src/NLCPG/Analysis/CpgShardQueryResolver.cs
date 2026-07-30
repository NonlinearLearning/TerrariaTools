using System.Collections.Concurrent;
using NL.Caching;
using NLCPG.Persistence;
using NLCPG.Model;

namespace NLCPG.Analysis;



//查询冻结的CPG分片
public sealed class CpgShardQueryResolver
{
    private readonly ICpgShardCatalog _catalog;
    private readonly ICpgShardStore _store;
    private readonly ByteBudgetLruCache<string, CpgFrozenShard> _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<CpgFrozenShard>>> _inflightLoads = new(StringComparer.Ordinal);

    // 绑定目录与存储读取器，并设置分片读取缓存上限。
    public CpgShardQueryResolver(ICpgShardCatalog catalog, ICpgShardStore store, long maxCachedBytes)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _cache = new ByteBudgetLruCache<string, CpgFrozenShard>(
            Math.Max(0, maxCachedBytes),
            StringComparer.Ordinal);
    }

    public CacheStatistics CacheStatistics => _cache.GetStatistics();

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

    // 返回冻结分片及其已验证位置元数据，供有界查询计量实际加载字节。
    public async Task<IReadOnlyList<CpgResolvedShard>> FindResolvedByNodeAsync(
        NodeId nodeId,
        CancellationToken cancellationToken)
    {
        var locations = await _catalog.FindByNodeAsync(nodeId.Value, cancellationToken);
        var shards = await OpenLocationsAsync(locations, cancellationToken);
        return locations
            .OrderBy(location => location.ShardId, StringComparer.Ordinal)
            .Zip(shards, static (location, shard) => new CpgResolvedShard(shard, location))
            .ToArray();
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
            if (_cache.TryGet(location.ShardId, out var cachedShard))
            {
                shards.Add(cachedShard);
                continue;
            }

            var shard = await LoadShardAsync(location, cancellationToken);
            shards.Add(shard);
        }

        return shards;
    }

    private async Task<CpgFrozenShard> LoadShardAsync(CpgShardLocation location, CancellationToken cancellationToken)
    {
        Lazy<Task<CpgFrozenShard>>? created = null;
        created = new Lazy<Task<CpgFrozenShard>>(
            () => ReadAndCacheAsync(location, created!),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var load = _inflightLoads.GetOrAdd(location.ShardId, created);
        return await load.Value.WaitAsync(cancellationToken);
    }

    private async Task<CpgFrozenShard> ReadAndCacheAsync(
        CpgShardLocation location,
        Lazy<Task<CpgFrozenShard>> load)
    {
        try
        {
            var shard = await _store.ReadAsync(location, CancellationToken.None);
            _cache.Set(location.ShardId, shard, location.ByteLength);
            return shard;
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<CpgFrozenShard>>>>)_inflightLoads).Remove(
                new KeyValuePair<string, Lazy<Task<CpgFrozenShard>>>(location.ShardId, load));
        }
    }

}

public sealed record CpgResolvedShard(CpgFrozenShard Shard, CpgShardLocation Location);
