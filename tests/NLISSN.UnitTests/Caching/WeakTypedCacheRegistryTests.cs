using NL.Caching;
using Xunit;

namespace NLISSN.Tests.Caching;

public sealed class WeakTypedCacheRegistryTests
{
    [Fact]
    public void GetOrCreate_SameReferenceKeyAndValueType_ReusesValue()
    {
        var registry = new WeakTypedCacheRegistry<object>();
        var key = new object();

        var first = registry.GetOrCreate(key, static _ => new CacheA("first"));
        var second = registry.GetOrCreate(key, static _ => new CacheA("second"));

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_DifferentKeyOrValueType_CreatesSeparateValues()
    {
        var registry = new WeakTypedCacheRegistry<object>();
        var firstKey = new object();
        var secondKey = new object();

        var firstA = registry.GetOrCreate(firstKey, static _ => new CacheA("first"));
        var firstB = registry.GetOrCreate(firstKey, static _ => new CacheB("second"));
        var secondA = registry.GetOrCreate(secondKey, static _ => new CacheA("third"));

        Assert.Equal("first", firstA.Value);
        Assert.Equal("second", firstB.Value);
        Assert.NotSame(firstA, secondA);
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentSameKeyAndValueType_InvokesFactoryOnce()
    {
        var registry = new WeakTypedCacheRegistry<object>();
        var key = new object();
        using var start = new ManualResetEventSlim(false);
        var factoryCallCount = 0;

        var requests = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return registry.GetOrCreate(key, _ =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    Thread.Sleep(100);
                    return new CacheA("value");
                });
            }))
            .ToArray();

        start.Set();
        var values = await Task.WhenAll(requests);

        Assert.Equal(1, Volatile.Read(ref factoryCallCount));
        Assert.All(values, value => Assert.Same(values[0], value));
    }

    [Fact]
    public void GetOrCreate_FactoryFails_SubsequentRequestCanRetry()
    {
        var registry = new WeakTypedCacheRegistry<object>();
        var key = new object();

        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate<CacheA>(
            key,
            _ => throw new InvalidOperationException("expected")));

        var value = registry.GetOrCreate(key, _ => new CacheA("retry"));

        Assert.Equal("retry", value.Value);
    }

    private sealed record CacheA(string Value);

    private sealed record CacheB(string Value);
}
