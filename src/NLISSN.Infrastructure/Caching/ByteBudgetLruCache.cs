namespace NL.Caching;

public sealed class ByteBudgetLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly long _maxCachedBytes;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _entries;
    private readonly LinkedList<CacheEntry> _recency = new();
    private long _cachedBytes;
    private long _evictionCount;
    private long _hitCount;
    private long _insertCount;
    private long _missCount;
    private long _rejectedCount;
    private long _replaceCount;

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

    public CacheStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new CacheStatistics(
                _hitCount,
                _missCount,
                _insertCount,
                _replaceCount,
                _evictionCount,
                _rejectedCount,
                _cachedBytes,
                _entries.Count);
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                _missCount++;
                value = default!;
                return false;
            }

            _hitCount++;
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
                _rejectedCount++;
                return false;
            }

            var replaced = false;
            if (_entries.TryGetValue(key, out var existingNode))
            {
                Remove(existingNode, isEviction: false);
                _replaceCount++;
                replaced = true;
            }

            while (_cachedBytes > _maxCachedBytes - byteWeight)
            {
                Remove(_recency.First!, isEviction: true);
            }

            var node = _recency.AddLast(new CacheEntry(key, value, byteWeight));
            _entries.Add(key, node);
            _cachedBytes += byteWeight;
            if (!replaced)
            {
                _insertCount++;
            }
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

    private void Remove(LinkedListNode<CacheEntry> node, bool isEviction)
    {
        _entries.Remove(node.Value.Key);
        _recency.Remove(node);
        _cachedBytes -= node.Value.ByteWeight;
        if (isEviction)
        {
            _evictionCount++;
        }
    }

    private sealed record CacheEntry(TKey Key, TValue Value, long ByteWeight);
}
