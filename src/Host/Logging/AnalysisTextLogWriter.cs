using System.Diagnostics;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application.Logging;

internal sealed class AnalysisTextLogWriter
{
    private readonly ITextLogSink _sink;
    private readonly TextLogFilter _filter;
    private readonly RunLogContext _context;
    private readonly string _src;

    public AnalysisTextLogWriter(
      ITextLogSink sink,
      TextLogFilter filter,
      RunLogContext context,
      string source)
    {
        _sink = sink;
        _filter = filter;
        _context = context;
        _src = source;
    }

    public void WriteResult(string filePath, PrototypeAnalysisResult result)
    {
        if (result.Timings is not null)
        {
            WritePhaseCompleted(filePath, "semantic-model", result.Timings.PreparationMilliseconds);
            WritePhaseCompleted(filePath, "cpg-build", result.Timings.CpgBuildMilliseconds);
            WritePhaseCompleted(filePath, "mark", result.Timings.MarkMilliseconds);
            WritePhaseCompleted(filePath, "propagate", result.Timings.PropagateMilliseconds);
            WritePhaseCompleted(filePath, "lift", result.Timings.LiftMilliseconds);
            WritePhaseCompleted(filePath, "decide", result.Timings.DecideMilliseconds);
            WritePhaseCompleted(filePath, "total", result.Timings.TotalMilliseconds);
        }

        WriteFileCompleted(filePath, result.Timings?.TotalMilliseconds ?? 0);
        WriteMemorySnapshot(filePath);
    }

    public void WriteIoSummary(TextLogFileSink sink)
    {
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Io,
          TextLogEventType.Summary,
          "io summary",
          sink.Path,
          new[]
          {
            new TextLogField("path", sink.Path),
            new TextLogField("records", sink.RecordsWritten)
          });
    }

    public void WriteIoFailure(Exception exception, TextLogFileSink sink)
    {
        Emit(
          TextLogLevel.Error,
          TextLogCategory.Io,
          TextLogEventType.WriterFailed,
          "io writer failed",
          sink.Path,
          new[]
          {
            new TextLogField("path", sink.Path),
            new TextLogField("errorType", exception.GetType().FullName),
            new TextLogField("error", exception.Message)
          });
    }

    public void WriteFileCompleted(string filePath, long elapsedMilliseconds)
    {
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.File,
          TextLogEventType.Completed,
          "file completed",
          filePath,
          new[]
          {
            new TextLogField("elapsedMs", elapsedMilliseconds)
          });
    }

    public void WritePhaseCompleted(string filePath, string phase, long elapsedMilliseconds)
    {
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Phase,
          TextLogEventType.Completed,
          "phase completed",
          filePath,
          new[]
          {
            new TextLogField("elapsedMs", elapsedMilliseconds)
          },
          phase);
    }

    public void WriteMemorySnapshot(string filePath)
    {
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var maxIoWorkers);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIoWorkers2);
        _ = maxIoWorkers;
        _ = maxIoWorkers2;
        var memoryInfo = GC.GetGCMemoryInfo();

        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Memory,
          TextLogEventType.Snapshot,
          "memory snapshot",
          filePath,
          new[]
          {
            new TextLogField("heapBytes", memoryInfo.HeapSizeBytes),
            new TextLogField("committedBytes", memoryInfo.TotalCommittedBytes),
            new TextLogField("fragmentedBytes", memoryInfo.FragmentedBytes),
            new TextLogField("wsBytes", Environment.WorkingSet),
            new TextLogField("privateBytes", Process.GetCurrentProcess().PrivateMemorySize64),
            new TextLogField("allocBytes", GC.GetTotalAllocatedBytes(precise: false)),
            new TextLogField("tpThreads", ThreadPool.ThreadCount),
            new TextLogField("tpPending", ThreadPool.PendingWorkItemCount),
            new TextLogField("tpCompleted", ThreadPool.CompletedWorkItemCount),
            new TextLogField("availableWorkers", availableWorkers),
            new TextLogField("maxWorkers", maxWorkers)
          });
    }

    private void Emit(
      TextLogLevel level,
      TextLogCategory category,
      TextLogEventType eventType,
      string message,
      string filePath,
      IReadOnlyList<TextLogField>? fields,
      string? phase = null)
    {
        var textLogEvent = new TextLogEvent(
          DateTimeOffset.UtcNow,
          level,
          category,
          eventType,
          message,
          _context.RunId,
          Operation: _context.Operation,
          InputKind: _context.InputKind,
          InputPath: _context.InputPath,
          Source: _src,
          FilePath: filePath,
          Phase: phase,
          Dop: _context.Dop,
          Fields: fields);

        if (_filter.Allows(textLogEvent))
        {
            _sink.Emit(textLogEvent);
        }
    }
}
