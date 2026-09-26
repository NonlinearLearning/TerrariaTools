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

/// Stable stage identifiers for fixed-worker WorkBatch execution.
public static class CpgWorkBatchPerformanceStageId
{
    public const string Syntax = "CPG.WorkBatch.Syntax";
    public const string Operation = "CPG.WorkBatch.Operation";
    public const string CallGraph = "CPG.WorkBatch.CallGraph";
    public const string MemberAccess = "CPG.WorkBatch.MemberAccess";
    public const string ControlFlow = "CPG.WorkBatch.ControlFlow";
    public const string DataFlow = "CPG.WorkBatch.DataFlow";
    public const string Dominance = "CPG.WorkBatch.Dominance";
    public const string ControlDependence = "CPG.WorkBatch.ControlDependence";
}

/// Immutable scheduling and output facts for one WorkBatch.
public sealed record CpgWorkBatchPerformanceEvent(
  string StageId,
  long BatchId,
  int StableOrder,
  int InputCount,
  int EstimatedCost,
  int EstimatedBytes,
  int OutputNodeCount,
  int OutputEdgeCount,
  int FragmentBytes,
  double QueueWaitMilliseconds,
  long ProcessingElapsedMilliseconds,
  long ReducerWaitElapsedMilliseconds,
  int WorkerIndex,
  int PeakActiveWorkerCount,
  int QueueHighWaterMark,
  int CompletedNotReducedHighWaterMark,
  string? RunId = null);

/// Receives optional WorkBatch diagnostics. Implementations must be fail-open.
public interface ICpgWorkBatchPerformanceEventSink
{
    void Record(CpgWorkBatchPerformanceEvent performanceEvent);
}

public static class CpgWorkBatchPerformanceEventSinkExtensions
{
    public static bool TryRecord(
      this ICpgWorkBatchPerformanceEventSink? sink,
      CpgWorkBatchPerformanceEvent performanceEvent)
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
            return false;
        }
    }
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
