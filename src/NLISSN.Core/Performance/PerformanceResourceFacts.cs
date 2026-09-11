namespace NLISSN.Core.Performance;

/// Resource counters whose attribution is explicit. Process-wide counters are
/// never presented as per-item values unless the scope is exclusive.
public sealed record PerformanceResourceFacts(
  long? AllocatedBytes,
  long? HeapBytes,
  long? WorkingSetBytes,
  int? Gen0Collections,
  int? Gen1Collections,
  int? Gen2Collections,
  int? PoolOperationCount,
  double? PoolQueueWaitMs,
  double? PoolQueueWaitMaxMs,
  int? PoolPeakActive,
  int? PoolPeakBuffer,
  PerformanceAttributionLevel Attribution = PerformanceAttributionLevel.Run,
  string? ErrorKind = null);
