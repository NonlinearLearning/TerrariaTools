namespace NLISSN.Core.Performance;

public enum PerformanceStatus
{
  Completed,
  Failed,
  Cancelled,
  Skipped,
  Unavailable,
  Unknown
}

public enum PerformanceMode
{
  Normal,
  Diagnostic,
  Profile,
  Benchmark
}

public enum PerformanceAttributionLevel
{
  None,
  Item,
  Stage,
  Run,
  Process
}
