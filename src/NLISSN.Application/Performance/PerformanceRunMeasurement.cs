using System.Diagnostics;
using NL.Concurrency;
using NLISSN.Core.Performance;
using NLISSN.Core.Pipeline;

namespace NLISSN.Application.Performance;

/// Captures process/run counters without claiming per-item resource ownership.
public sealed class PerformanceRunMeasurement
{
  private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
  private readonly long _allocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
  private readonly int _gen0 = GC.CollectionCount(0);
  private readonly int _gen1 = GC.CollectionCount(1);
  private readonly int _gen2 = GC.CollectionCount(2);
  private readonly long _workingSet = Process.GetCurrentProcess().WorkingSet64;

  public PerformanceStageSample CompleteRoot(
    string stageId,
    string? itemId,
    AnalysisRuntime runtime,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    _stopwatch.Stop();
    var operations = runtime.ConcurrencyTelemetry.Operations;
    return new PerformanceStageSample(
      stageId,
      null,
      itemId,
      _stopwatch.ElapsedMilliseconds,
      operations.Count == 0
        ? null
        : operations.Sum(operation => (long)operation.Elapsed.TotalMilliseconds),
      status,
      errorKind,
      attribution: PerformanceAttributionLevel.Run,
      queueWaitMs: operations.Count == 0
        ? null
        : operations.Sum(operation => operation.QueueWait.TotalMilliseconds),
      peakActive: operations.Count == 0 ? null : operations.Max(operation => operation.PeakActiveWorkItemCount),
      peakBuffer: operations.Count == 0 ? null : operations.Max(operation => operation.PeakCompletedBufferItemCount));
  }

  public PerformanceResourceFacts CompleteResources(AnalysisRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    var operations = runtime.ConcurrencyTelemetry.Operations;
    var memory = GC.GetGCMemoryInfo();
    var process = Process.GetCurrentProcess();
    return new PerformanceResourceFacts(
      GC.GetTotalAllocatedBytes(precise: false) - _allocatedBytes,
      memory.HeapSizeBytes > 0 ? memory.HeapSizeBytes : GC.GetTotalMemory(forceFullCollection: false),
      process.WorkingSet64,
      GC.CollectionCount(0) - _gen0,
      GC.CollectionCount(1) - _gen1,
      GC.CollectionCount(2) - _gen2,
      operations.Count,
      operations.Count == 0 ? null : operations.Sum(operation => operation.QueueWait.TotalMilliseconds),
      operations.Count == 0 ? null : operations.Max(operation => operation.QueueWait.TotalMilliseconds),
      operations.Count == 0 ? null : operations.Max(operation => operation.PeakActiveWorkItemCount),
      operations.Count == 0 ? null : operations.Max(operation => operation.PeakCompletedBufferItemCount),
      PerformanceAttributionLevel.Run);
  }
}
