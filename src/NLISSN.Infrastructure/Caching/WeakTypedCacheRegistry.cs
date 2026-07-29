using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NL.Caching;

public sealed class WeakTypedCacheRegistry<TKey>
    where TKey : class
{
    private readonly ConditionalWeakTable<TKey, ConcurrentDictionary<Type, object>> _entries = new();

    public TValue GetOrCreate<TValue>(TKey key, Func<TKey, TValue> factory)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var values = _entries.GetValue(
            key,
            static _ => new ConcurrentDictionary<Type, object>());
        return (TValue)values.GetOrAdd(typeof(TValue), _ => factory(key));
    }
}
