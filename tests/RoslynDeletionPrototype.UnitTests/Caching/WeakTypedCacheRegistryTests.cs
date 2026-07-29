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

    private sealed record CacheA(string Value);

    private sealed record CacheB(string Value);
}
