using System.Diagnostics;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Analysis;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application.Logging;

internal sealed class RunTextLogWriter
{
    private readonly ITextLogSink _sink;
    private readonly TextLogFilter _filter;
    private readonly RunLogContext _context;
    private readonly string _src;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public RunTextLogWriter(
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

    public void Start()
    {
        Emit(
          TextLogLevel.Info,
          TextLogCategory.Run,
          TextLogEventType.Started,
          "analysis started");
    }

    public void Sample()
    {
        var memory = CaptureMemorySnapshot();
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Run,
          TextLogEventType.Sampled,
          "analysis sampled",
          fields: new[]
          {
            new TextLogField("elapsedMs", _stopwatch.ElapsedMilliseconds),
            new TextLogField("allocBytes", GC.GetTotalAllocatedBytes(precise: false)),
            new TextLogField("gen0", GC.CollectionCount(0)),
            new TextLogField("gen1", GC.CollectionCount(1)),
            new TextLogField("gen2", GC.CollectionCount(2)),
            new TextLogField("heapBytes", memory.HeapBytes),
            new TextLogField("wsBytes", memory.WorkingSetBytes),
            new TextLogField("tpThreads", memory.ThreadCount),
            new TextLogField("tpPending", memory.PendingWorkItemCount),
            new TextLogField("tpCompleted", memory.CompletedWorkItemCount),
            new TextLogField("availableWorkers", memory.AvailableWorkers),
            new TextLogField("maxWorkers", memory.MaxWorkers)
          });
    }

    public void Complete(PrototypeAnalysisResult result, string status, string inputKind)
    {
        var fields = new List<TextLogField>();
        if (result.Stats?.AnalyzedFileCount is not null)
        {
            fields.Add(new TextLogField("files", $"{result.Stats.AnalyzedFileCount}/{result.Stats.ScannedFileCount}"));
        }
        else if (result.Stats is not null)
        {
            fields.Add(new TextLogField("files", result.Stats.ScannedFileCount));
        }

        fields.Add(new TextLogField("elapsedMs", _stopwatch.ElapsedMilliseconds));
        fields.Add(new TextLogField("edits", result.Edits.Count));
        fields.Add(new TextLogField("diags", result.Diagnostics?.Count ?? 0));
        fields.Add(new TextLogField("status", status));
        Emit(
          TextLogLevel.Info,
          TextLogCategory.Run,
          TextLogEventType.Completed,
          "analysis completed",
          inputKind,
          fields);
    }

    public void WriteDiagnostics(IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        var errorCount = diagnostics.Count;
        foreach (var diagnostic in diagnostics)
        {
            Emit(
              TextLogLevel.Warn,
              TextLogCategory.Diag,
              TextLogEventType.Error,
              "diagnostic error",
              fields: new[]
              {
                new TextLogField("file", diagnostic.FilePath),
                new TextLogField("diagId", diagnostic.Id),
                new TextLogField("span", $"{diagnostic.Start}-{diagnostic.End}"),
                new TextLogField("text", diagnostic.Message)
              });
        }

        Emit(
          TextLogLevel.Info,
          TextLogCategory.Diag,
          TextLogEventType.Summary,
          "diagnostic summary",
          fields: new[]
          {
            new TextLogField("diags", errorCount),
            new TextLogField("warnings", 0),
            new TextLogField("errors", errorCount)
          });
    }

    public void Fail(Exception exception, string inputKind)
    {
        Emit(
          TextLogLevel.Error,
          TextLogCategory.Run,
          TextLogEventType.Failed,
          "analysis failed",
          inputKind,
          new[]
          {
            new TextLogField("elapsedMs", _stopwatch.ElapsedMilliseconds),
            new TextLogField("status", "failed"),
            new TextLogField("errorType", exception.GetType().FullName),
            new TextLogField("error", exception.Message)
          });
    }

    public void WriteCpgSummary(RoslynCpgBuildTelemetry telemetry)
    {
        var freeze = telemetry.FreezeTelemetry;
        var operationWindow = telemetry.OperationOrderedWindow ?? RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
        var cfgSensitiveWindow = telemetry.CfgSensitiveOrderedWindow ?? RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Cpg,
          TextLogEventType.Summary,
          "cpg build summary",
          fields: new[]
          {
            new TextLogField("nodes", telemetry.GraphNodeCount),
            new TextLogField("edges", telemetry.GraphEdgeCount),
            new TextLogField("partitions", telemetry.PartitionCount),
            new TextLogField("opMs", telemetry.OperationBuildElapsedMilliseconds),
            new TextLogField("syntaxMs", telemetry.SyntaxBuildElapsedMilliseconds),
            new TextLogField("dataFlowMs", telemetry.DataFlowBuildElapsedMilliseconds),
            new TextLogField("freezeMs", telemetry.FreezeQueryIndexElapsedMilliseconds),
            new TextLogField("freezeAssignNodeIdsMs", freeze.AssignDeterministicNodeIdsElapsedMilliseconds),
            new TextLogField("freezeCreateAnchorsMs", freeze.CreateAnchorsElapsedMilliseconds),
            new TextLogField("freezeCreateNodeIdTableMs", freeze.CreateNodeIdTableElapsedMilliseconds),
            new TextLogField("freezeRemapNodesMs", freeze.RemapNodesElapsedMilliseconds),
            new TextLogField("freezeRemapEdgesMs", freeze.RemapEdgesElapsedMilliseconds),
            new TextLogField("freezeEdgeBucketsMs", freeze.PopulateEdgeIndexBucketsElapsedMilliseconds),
            new TextLogField("freezeOrderEdgesMs", freeze.OrderEdgesElapsedMilliseconds),
            new TextLogField("freezeOrderNodesMs", freeze.OrderNodesElapsedMilliseconds),
            new TextLogField("freezeSnapshotHashMs", freeze.SnapshotHashElapsedMilliseconds),
            new TextLogField("freezeAdjacencyMs", freeze.BuildAdjacencyElapsedMilliseconds),
            new TextLogField("freezeKindAdjacencyMs", freeze.BuildKindAdjacencyElapsedMilliseconds),
            new TextLogField("freezeEdgeKindIndexMs", freeze.BuildEdgeKindIndexElapsedMilliseconds),
            new TextLogField("freezeNodeKindIndexMs", freeze.BuildNodeKindIndexElapsedMilliseconds),
            new TextLogField("freezeFilePathIndexMs", freeze.BuildFilePathIndexElapsedMilliseconds),
            new TextLogField("operationActiveWorkerPeak", operationWindow.ActiveWorkerPeak),
            new TextLogField("operationCompletedBufferPeak", operationWindow.CompletedButUncommittedPeak),
            new TextLogField("operationCompletedRecordPeak", operationWindow.CompletedRecordCountPeak),
            new TextLogField("operationCommitWaitMs", operationWindow.CommitWaitMilliseconds),
            new TextLogField("operationWindowBlockedMs", operationWindow.WindowBlockedMilliseconds),
            new TextLogField("cfgActiveWorkerPeak", cfgSensitiveWindow.ActiveWorkerPeak),
            new TextLogField("cfgCompletedBufferPeak", cfgSensitiveWindow.CompletedButUncommittedPeak),
            new TextLogField("cfgCommitWaitMs", cfgSensitiveWindow.CommitWaitMilliseconds),
            new TextLogField("cfgWindowBlockedMs", cfgSensitiveWindow.WindowBlockedMilliseconds)
          });
    }

    public void WriteMarkSummary(MarkAnalysisTelemetry telemetry)
    {
        var slowestRule = telemetry.RuleTelemetry
          .OrderByDescending(item => item.ElapsedMilliseconds)
          .FirstOrDefault();
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Mark,
          TextLogEventType.Summary,
          "mark summary",
          fields: new[]
          {
            new TextLogField("rules", telemetry.RuleTelemetry.Count),
            new TextLogField("slowestRule", slowestRule?.RuleId),
            new TextLogField("slowestMs", telemetry.RuleTelemetry.Count == 0 ? 0 : telemetry.RuleTelemetry.Max(item => item.ElapsedMilliseconds)),
            new TextLogField("cacheHits", telemetry.OperationLookupCacheHitCount + telemetry.GraphBindingIndexHitCount),
            new TextLogField("cacheMisses", telemetry.OperationLookupCacheMissCount + telemetry.GraphBindingIndexMissCount)
          });
    }

    public void WriteIoSummary(TextLogFileSink sink)
    {
        Emit(
          TextLogLevel.Debug,
          TextLogCategory.Io,
          TextLogEventType.Summary,
          "io summary",
          fields: new[]
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
          fields: new[]
          {
            new TextLogField("path", sink.Path),
            new TextLogField("errorType", exception.GetType().FullName),
            new TextLogField("error", exception.Message)
          });
    }

    public void Flush()
    {
        _sink.Flush();
    }

    private void Emit(
      TextLogLevel level,
      TextLogCategory category,
      TextLogEventType eventType,
      string message,
      string? inputKind = null,
      IReadOnlyList<TextLogField>? fields = null)
    {
        var textLogEvent = new TextLogEvent(
          DateTimeOffset.UtcNow,
          level,
          category,
          eventType,
          message,
          _context.RunId,
          Operation: _context.Operation,
          InputKind: inputKind ?? _context.InputKind,
          InputPath: _context.InputPath,
          Source: _src,
          Dop: _context.Dop,
          Fields: fields);

        if (_filter.Allows(textLogEvent))
        {
            _sink.Emit(textLogEvent);
        }
    }

    private static (long HeapBytes, long WorkingSetBytes, int ThreadCount, long PendingWorkItemCount, long CompletedWorkItemCount, int AvailableWorkers, int MaxWorkers) CaptureMemorySnapshot()
    {
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIoWorkers);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIoWorkers);
        _ = availableIoWorkers;
        _ = maxIoWorkers;
        return (
          GC.GetGCMemoryInfo().HeapSizeBytes,
          Environment.WorkingSet,
          ThreadPool.ThreadCount,
          ThreadPool.PendingWorkItemCount,
          ThreadPool.CompletedWorkItemCount,
          availableWorkers,
          maxWorkers);
    }
}
