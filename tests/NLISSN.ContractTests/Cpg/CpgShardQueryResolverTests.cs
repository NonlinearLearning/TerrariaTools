using NLCPG.Analysis;
using NLCPG.Model;
using NLCPG.Persistence;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgShardQueryResolverTests
{
    [Fact]
    public async Task FindByNodeAsync_ConcurrentSameShard_UsesOnePhysicalRead()
    {
        var location = CreateLocation();
        var catalog = new TestCatalog(location);
        var store = new TestShardStore(CreateShard(), blockReads: true);
        var resolver = new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1_024);

        var first = resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None);
        await store.ReadStarted;
        var second = resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None);
        await catalog.WaitForNodeLookupsAsync(2);

        Assert.Equal(1, store.ReadCount);
        store.ReleaseRead();

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Single(result));
        Assert.Equal(1, store.ReadCount);
    }

    [Fact]
    public async Task FindByNodeAsync_WaiterIsCanceled_SharedReadCompletesForOtherWaiter()
    {
        var location = CreateLocation();
        var catalog = new TestCatalog(location);
        var store = new TestShardStore(CreateShard(), blockReads: true);
        var resolver = new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1_024);
        using var cancellationSource = new CancellationTokenSource();

        var first = resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None);
        await store.ReadStarted;
        var canceledWaiter = resolver.FindByNodeAsync(new NodeId(1), cancellationSource.Token);
        await catalog.WaitForNodeLookupsAsync(2);
        cancellationSource.Cancel();
        store.ReleaseRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            canceledWaiter);

        Assert.Single(await first);
        Assert.Equal(1, store.ReadCount);
    }

    [Fact]
    public async Task FindByNodeAsync_LoadFails_SubsequentRequestRetries()
    {
        var location = CreateLocation();
        var catalog = new TestCatalog(location);
        var store = new TestShardStore(CreateShard());
        store.FailNextRead();
        var resolver = new CpgShardQueryResolver(catalog, store, maxCachedBytes: 1_024);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None));

        Assert.Single(await resolver.FindByNodeAsync(new NodeId(1), CancellationToken.None));
        Assert.Equal(2, store.ReadCount);
    }

    private static CpgShardLocation CreateLocation()
    {
        return new CpgShardLocation("shard", "shard.cpgbin", "hash", 128, CpgShardStatus.Complete);
    }

    private static CpgFrozenShard CreateShard()
    {
        var file = new CpgFileKey("project", "input.cs", "source");
        var fragment = new CpgFragmentKey("method", 0, 10, "fragment");
        var lookup = new CpgShardLookup(file, fragment, 1, "profile");
        var node = new CpgFrozenNode(0, 1, "Operation", "input.cs", 0, 1, "Operation", "node", null, null, false);
        return new CpgFrozenShard(lookup, new[] { node }, Array.Empty<CpgFrozenEdge>(), Array.Empty<CpgSymbolLocation>());
    }

    private sealed class TestCatalog : ICpgShardCatalog
    {
        private readonly CpgShardLocation _location;
        private readonly TaskCompletionSource _secondNodeLookup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _nodeLookupCount;

        public TestCatalog(CpgShardLocation location)
        {
            _location = location;
        }

        public async Task WaitForNodeLookupsAsync(int expectedCount)
        {
            if (Volatile.Read(ref _nodeLookupCount) >= expectedCount)
            {
                return;
            }

            await _secondNodeLookup.Task;
        }

        public Task<CpgShardLease?> TryAcquireAsync(CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CpgReusableShardLease?> TryAcquireReusableAsync(CpgReusableFragmentKey key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CpgShardLocation>> FindByFileAsync(CpgFileKey fileKey, int schemaVersion, string profileHash, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CpgShardLocation>> FindByNodeAsync(uint nodeId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _nodeLookupCount) == 2)
            {
                _secondNodeLookup.TrySetResult();
            }

            return Task.FromResult<IReadOnlyList<CpgShardLocation>>(new[] { _location });
        }

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

    private sealed class TestShardStore : ICpgShardStore
    {
        private readonly bool _blockReads;
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CpgFrozenShard _shard;
        private int _failNextRead;
        private int _readCount;

        public TestShardStore(CpgFrozenShard shard, bool blockReads = false)
        {
            _shard = shard;
            _blockReads = blockReads;
        }

        public Task ReadStarted => _readStarted.Task;

        public int ReadCount => Volatile.Read(ref _readCount);

        public void FailNextRead()
        {
            Interlocked.Exchange(ref _failNextRead, 1);
        }

        public void ReleaseRead()
        {
            _releaseRead.TrySetResult();
        }

        public Task<CpgShardLocation> WriteAsync(CpgFrozenShard shard, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<CpgFrozenShard> ReadAsync(CpgShardLocation location, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            _readStarted.TrySetResult();
            if (_blockReads)
            {
                await _releaseRead.Task;
            }

            if (Interlocked.Exchange(ref _failNextRead, 0) == 1)
            {
                throw new InvalidDataException("Read failed.");
            }

            return _shard;
        }

        public Task<CpgFrozenShard?> TryReadAsync(CpgShardLocation location, CpgShardLookup lookup, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(CpgFrozenShard Shard, CpgShardLocation Location)> ReadFromPathAsync(string shardPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
