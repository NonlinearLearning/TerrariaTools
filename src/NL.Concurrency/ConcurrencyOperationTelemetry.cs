using System.Collections.Concurrent;

namespace NL.Concurrency;

public enum ConcurrencyOperationKind
{
    OrderedSelection,
    CpuBoundOrderedSelection,
    OrderedCommit,
    TwoStageOrderedCommit,
    ForEach,
    DependencyGraph,
}

public sealed record ConcurrencyOperationTelemetry(
  ConcurrencyOperationKind OperationKind,
  int SourceCount,
  int RequestedMaxDegreeOfParallelism,
  int PeakActiveWorkItemCount,
  int PeakReadyWorkItemCount,
  int PeakCompletedBufferItemCount,
  int PeakRetainedRecordCount,
  ConcurrencyWorkClass WorkClass,
  long PeakReservedByteCount,
  TimeSpan QueueWait,
  ConcurrencyAdmissionReason? AdmissionReason,
  TimeSpan Elapsed,
  bool WasCanceled,
  TimeSpan FailureDrainElapsed);

public interface IConcurrencyPoolTelemetrySink
{
    void Record(ConcurrencyOperationTelemetry operation);
}

public sealed class ConcurrencyOperationTelemetryCollector : IConcurrencyPoolTelemetrySink
{
    private readonly ConcurrentQueue<ConcurrencyOperationTelemetry> _operations = new();

    public IReadOnlyList<ConcurrencyOperationTelemetry> Operations => _operations.ToArray();

    public void Record(ConcurrencyOperationTelemetry operation)
    {
        _operations.Enqueue(operation);
    }
}
