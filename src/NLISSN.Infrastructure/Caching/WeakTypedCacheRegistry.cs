using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NL.Caching;

public sealed class WeakTypedCacheRegistry<TKey>
    where TKey : class
{
    private readonly ConditionalWeakTable<TKey, ConcurrentDictionary<Type, Lazy<object>>> _entries = new();

    public TValue GetOrCreate<TValue>(TKey key, Func<TKey, TValue> factory)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var values = _entries.GetValue(
            key,
            static _ => new ConcurrentDictionary<Type, Lazy<object>>());
        var type = typeof(TValue);
        if (!values.TryGetValue(type, out var cachedValue))
        {
            var candidate = new Lazy<object>(
              () => factory(key),
              LazyThreadSafetyMode.ExecutionAndPublication);
            cachedValue = values.GetOrAdd(type, candidate);
        }

        try
        {
            return (TValue)cachedValue.Value;
        }
        catch
        {
            ((ICollection<KeyValuePair<Type, Lazy<object>>>)values).Remove(
                new KeyValuePair<Type, Lazy<object>>(type, cachedValue));
            throw;
        }
    }
}
