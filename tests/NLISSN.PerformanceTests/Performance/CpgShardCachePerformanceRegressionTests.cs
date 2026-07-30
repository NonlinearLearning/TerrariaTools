using System.Diagnostics;
using NLCPG.Analysis;
using NLCPG.Model;
using NLCPG.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

public sealed class CpgShardCachePerformanceRegressionTests
{
    private readonly ITestOutputHelper _output;

    public CpgShardCachePerformanceRegressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FindByNodeAsync_WarmedCache_ReducesPhysicalReadsAndPreservesResult()
    {
        var location = new CpgShardLocation("shard", "shard.cpgbin", "hash", 128, CpgShardStatus.Complete);
        var shard = CreateShard();
        var store = new CountingShardStore(shard);
        var resolver = new CpgShardQueryResolver(new SingleLocationCatalog(location), store, maxCachedBytes: 1_024);

        var cold = await MeasureAsync(resolver, store);
        var warm = await MeasureAsync(resolver, store);

        Assert.Equal(cold.ShardIds, warm.ShardIds);
        Assert.Equal(1, cold.PhysicalReads);
        Assert.Equal(0, warm.PhysicalReads);
        Assert.Equal(1, warm.Statistics.HitCount);
        Assert.Equal(1, warm.Statistics.MissCount);
        _output.WriteLine(
            $"cold reads={cold.PhysicalReads}; allocated={cold.AllocatedBytes}; elapsed-ms={cold.ElapsedMilliseconds}; " +
            $"warm reads={warm.PhysicalReads}; allocated={warm.AllocatedBytes}; elapsed-ms={warm.ElapsedMilliseconds}; " +
            $"serialized-bytes={location.ByteLength}; retained-bytes={warm.Statistics.CachedBytes}");
    }

    private static async Task<CacheMeasurement> MeasureAsync(CpgShardQueryResolver resolver, CountingShardStore store)
    {
        var readsBefore = store.ReadCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var shards = await resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new CacheMeasurement(
            store.ReadCount - readsBefore,
            allocatedBytes,
            stopwatch.ElapsedMilliseconds,
            shards.Select(shard => shard.Lookup.Fragment.FragmentHash).ToArray(),
            resolver.CacheStatistics);
    }

    private static CpgFrozenShard CreateShard()
    {
        var file = new CpgFileKey("project", "input.cs", "source");
        var fragment = new CpgFragmentKey("method", 0, 10, "fragment");
        var lookup = new CpgShardLookup(file, fragment, 1, "profile");
        var node = new CpgFrozenNode(0, 1, "Operation", "input.cs", 0, 1, "Operation", "node", null, null, false);
        return new CpgFrozenShard(lookup, new[] { node }, Array.Empty<CpgFrozenEdge>(), Array.Empty<CpgSymbolLocation>());
    }

    private sealed record CacheMeasurement(long PhysicalReads, long AllocatedBytes, long ElapsedMilliseconds, string[] ShardIds, NL.Caching.CacheStatistics Statistics);

    private sealed class SingleLocationCatalog : ICpgShardCatalog
    {
        private readonly CpgShardLocation _location;

        public SingleLocationCatalog(CpgShardLocation location)
        {
            _location = location;
        }

        public Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CpgReusableShardLease?> TryAcquireReusableAsync(CpgReusableFragmentKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(CpgFileKey fileKey, int schemaVersion, string profileHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(uint nodeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CpgShardLocation>>(new[] { _location });
        public Task<IReadOnlyList<CpgShardLocation>> FindBySymbolAsync(CpgSymbolLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CpgShardLocation>> FindBySpanAsync(CpgSpanLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PublishAsync(CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> BeginBuildAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StageReusableAsync(string buildId, CpgShardLease lease, CpgFrozenShard shard, CpgReusableFragmentKey reusableKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteBuildAsync(string buildId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateBuildAsync(string buildId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateFileAsync(CpgFileKey fileKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingShardStore : ICpgShardStore
    {
        private readonly CpgFrozenShard _shard;
        private int _readCount;

        public CountingShardStore(CpgFrozenShard shard)
        {
            _shard = shard;
        }

        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<CpgShardLocation> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            return Task.FromResult(_shard);
        }

        public Task<CpgFrozenShard?> TryReadAsync(CpgShardLocation location, CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(string shardPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
