namespace NL.Caching;

public sealed class ByteBudgetLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly long _maxCachedBytes;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _entries;
    private readonly LinkedList<CacheEntry> _recency = new();
    private long _cachedBytes;

    public ByteBudgetLruCache(long maxCachedBytes, IEqualityComparer<TKey>? comparer = null)
    {
        if (maxCachedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCachedBytes));
        }

        _maxCachedBytes = maxCachedBytes;
        _entries = new Dictionary<TKey, LinkedListNode<CacheEntry>>(comparer);
    }

    public long CachedBytes
    {
        get
        {
            lock (_gate)
            {
                return _cachedBytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                value = default!;
                return false;
            }

            MoveToMru(node);
            value = node.Value.Value;
            return true;
        }
    }

    public bool Set(TKey key, TValue value, long byteWeight)
    {
        if (byteWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteWeight));
        }

        lock (_gate)
        {
            if (byteWeight == 0 || byteWeight > _maxCachedBytes)
            {
                return false;
            }

            if (_entries.TryGetValue(key, out var existingNode))
            {
                Remove(existingNode);
            }

            while (_cachedBytes > _maxCachedBytes - byteWeight)
            {
                Remove(_recency.First!);
            }

            var node = _recency.AddLast(new CacheEntry(key, value, byteWeight));
            _entries.Add(key, node);
            _cachedBytes += byteWeight;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            _cachedBytes = 0;
        }
    }

    private void MoveToMru(LinkedListNode<CacheEntry> node)
    {
        _recency.Remove(node);
        _recency.AddLast(node);
    }

    private void Remove(LinkedListNode<CacheEntry> node)
    {
        _entries.Remove(node.Value.Key);
        _recency.Remove(node);
        _cachedBytes -= node.Value.ByteWeight;
    }

    private sealed record CacheEntry(TKey Key, TValue Value, long ByteWeight);
}
