using NL.Caching;
using Xunit;

namespace NLISSN.Tests.Caching;

public sealed class ByteBudgetLruCacheTests
{
    [Fact]
    public void Set_ZeroBudget_DoesNotRetainEntry()
    {
        var cache = new ByteBudgetLruCache<string, string>(0);

        var cached = cache.Set("key", "value", 1);

        Assert.False(cached);
        Assert.False(cache.TryGet("key", out _));
        Assert.Equal(0, cache.CachedBytes);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Set_OversizedEntry_DoesNotEvictFittingResident()
    {
        var cache = new ByteBudgetLruCache<string, string>(10);
        cache.Set("resident", "value", 5);

        var cached = cache.Set("oversized", "value", 11);

        Assert.False(cached);
        Assert.True(cache.TryGet("resident", out var resident));
        Assert.Equal("value", resident);
        Assert.False(cache.TryGet("oversized", out _));
        Assert.Equal(5, cache.CachedBytes);
    }

    [Fact]
    public void TryGet_AccessedEntry_PromotesItAboveOlderEntry()
    {
        var cache = new ByteBudgetLruCache<string, string>(10);
        cache.Set("first", "first", 5);
        cache.Set("second", "second", 5);

        Assert.True(cache.TryGet("first", out _));
        cache.Set("third", "third", 5);

        Assert.True(cache.TryGet("first", out _));
        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("third", out _));
    }

    [Fact]
    public void Set_ExistingKey_ReplacesValueAndWeight()
    {
        var cache = new ByteBudgetLruCache<string, string>(10);
        cache.Set("key", "first", 3);

        var cached = cache.Set("key", "second", 7);

        Assert.True(cached);
        Assert.True(cache.TryGet("key", out var value));
        Assert.Equal("second", value);
        Assert.Equal(7, cache.CachedBytes);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Set_NegativeWeight_ThrowsArgumentOutOfRangeException()
    {
        var cache = new ByteBudgetLruCache<string, string>(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Set("key", "value", -1));
    }

    [Fact]
    public void GetStatistics_OperationsOccur_ReturnsConsistentSnapshot()
    {
        var cache = new ByteBudgetLruCache<string, string>(10);
        cache.Set("first", "first", 5);
        cache.TryGet("missing", out _);
        cache.TryGet("first", out _);
        cache.Set("first", "replacement", 4);
        cache.Set("second", "second", 7);
        cache.Set("zero", "zero", 0);
        cache.Set("oversized", "oversized", 11);

        var statistics = cache.GetStatistics();

        Assert.Equal(1, statistics.HitCount);
        Assert.Equal(1, statistics.MissCount);
        Assert.Equal(2, statistics.InsertCount);
        Assert.Equal(1, statistics.ReplaceCount);
        Assert.Equal(1, statistics.EvictionCount);
        Assert.Equal(2, statistics.RejectedCount);
        Assert.Equal(7, statistics.CachedBytes);
        Assert.Equal(1, statistics.Count);
    }

    [Fact]
    public void Set_ParallelReadsAndWrites_PreservesBudgetInvariant()
    {
        var cache = new ByteBudgetLruCache<int, int>(32);

        Parallel.For(0, 1_000, index =>
        {
            cache.Set(index % 16, index, 2);
            cache.TryGet((index + 1) % 16, out _);
        });

        Assert.InRange(cache.CachedBytes, 0, 32);
        Assert.InRange(cache.Count, 0, 16);
    }
}
