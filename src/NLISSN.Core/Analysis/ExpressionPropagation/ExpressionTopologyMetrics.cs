namespace NLISSN.Core.Analysis.ExpressionPropagation;

/// <summary>Session-local counters for topology and semantic-overlay cache use.</summary>
public sealed record ExpressionTopologyMetrics(
    long AnalyzeCount,
    int CachedPathCount,
    long OperationCacheHits,
    long OperationCacheMisses);
