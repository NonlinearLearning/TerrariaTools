namespace NLCPG.Builder;

/// Controls the optional high-cardinality diagnostics emitted for CPG partitions.
public enum NLCPGPerformanceDiagnosticsMode
{
    Disabled,
    Diagnostic,
}

/// Stable protocol names for partition collection and ordered materialization.
public static class PartitionPerformanceStageId
{
    public const string SyntaxCollection = "CPG.Syntax.Collection";
    public const string SyntaxMaterialization = "CPG.Syntax.Materialization";
    public const string OperationCollection = "CPG.Operation.Collection";
    public const string OperationMaterialization = "CPG.Operation.Materialization";
}

/// Immutable diagnostic facts for one partition boundary.
public sealed record PartitionPerformanceEvent(
  string StageId,
  string PartitionId,
  int PartitionIndex,
  int InputCount,
  int OutputCount,
  long WallElapsedMs,
  long AccumulatedElapsedMs,
  double? QueueWaitMs,
  int? RequestedMaxDegreeOfParallelism,
  int? PeakActiveWorkItemCount,
  int? PeakBufferCount,
  string? AdmissionReason,
  string? RunId = null)
{
    public long WallElapsedMilliseconds => WallElapsedMs;

    public long AccumulatedElapsedMilliseconds => AccumulatedElapsedMs;

    public double? QueueWaitMilliseconds => QueueWaitMs;
}

/// Receives optional partition diagnostics. Implementations must not own graph state.
public interface IPartitionPerformanceEventSink
{
    void Record(PartitionPerformanceEvent performanceEvent);
}

public static class PartitionPerformanceEventSinkExtensions
{
    public static bool TryRecord(
      this IPartitionPerformanceEventSink? sink,
      PartitionPerformanceEvent performanceEvent)
    {
        if (sink is null)
        {
            return false;
        }

        try
        {
            sink.Record(performanceEvent);
            return true;
        }
        catch
        {
            // Partition diagnostics are fail-open and cannot affect graph construction.
            return false;
        }
    }
}
