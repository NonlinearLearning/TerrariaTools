namespace NL.Caching;

public readonly record struct CacheStatistics(
    long HitCount,
    long MissCount,
    long InsertCount,
    long ReplaceCount,
    long EvictionCount,
    long RejectedCount,
    long CachedBytes,
    int Count);
