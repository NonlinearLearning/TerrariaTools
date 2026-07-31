using System.Diagnostics;
using NL.Concurrency;
using NLISSN.Cli.Parsing;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;
using NLISSN.Logging;

namespace NLISSN.Telemetry;

/// <summary>
/// Writes opt-in process and pool metrics for one CLI analysis without affecting scheduling.
/// </summary>
internal sealed class RuntimeMeasurementLog : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private readonly TextLogFileSink _sink;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly CancellationTokenSource _samplingCancellation = new();
    private readonly Task _samplingTask;
    private readonly long _allocatedBytesAtStart;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly string? _inputPath;
    private readonly string _inputKind;
    private readonly int _dop;
    private int _completed;

    private RuntimeMeasurementLog(
        string path,
        IReadOnlyDictionary<string, string> options,
        string? inputPath,
        AnalysisRuntime runtime)
    {
        _sink = TextLogFileSink.Create(
          path,
          new TextLogFormatter(),
          TextLogFilter.CreateRuntimeFilter(options));
        _inputPath = inputPath;
        _inputKind = inputPath is not null && Directory.Exists(inputPath) ? "directory" : "file";
        _dop = runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism;
        _allocatedBytesAtStart = GC.GetTotalAllocatedBytes(precise: false);
        Emit(TextLogEventType.Started, "Analysis started", Array.Empty<TextLogField>());
        EmitSample();
        _samplingTask = SampleUntilCompletedAsync();
    }

    public static RuntimeMeasurementLog? TryCreate(
        IReadOnlyDictionary<string, string> options,
        string? inputPath,
        AnalysisRuntime runtime)
    {
        var path = ApplicationOptions.ResolveRuntimeLogPath(options);
        return path is null ? null : new RuntimeMeasurementLog(path, options, inputPath, runtime);
    }

    public async Task CompleteAsync(
        PrototypeAnalysisResult result,
        AnalysisRuntime runtime)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        await StopSamplingAsync();
        EmitSample();
        var operations = runtime.ConcurrencyTelemetry.Operations;
        EmitPoolOperations(operations);
        Emit(
          TextLogEventType.Completed,
          "Analysis completed",
          CreateTerminalFields(result, operations, status: "completed"));
    }

    public async Task FailAsync(Exception exception, AnalysisRuntime runtime)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        await StopSamplingAsync();
        EmitSample();
        var operations = runtime.ConcurrencyTelemetry.Operations;
        EmitPoolOperations(operations);
        var fields = CreatePoolFields(operations).ToList();
        fields.Add(new TextLogField("status", "failed"));
        fields.Add(new TextLogField("exception", exception.GetType().FullName));
        Emit(TextLogEventType.Failed, "Analysis failed", fields);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            await StopSamplingAsync();
        }

        _samplingCancellation.Dispose();
        try
        {
            await _sink.DisposeAsync();
        }
        catch
        {
            // A measurement artifact must not turn a completed analysis into a failure.
        }
    }

    private async Task SampleUntilCompletedAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(SampleInterval, _samplingCancellation.Token);
                EmitSample();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StopSamplingAsync()
    {
        _samplingCancellation.Cancel();
        await _samplingTask;
    }

    private void EmitSample()
    {
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var maximumWorkerThreads, out var maximumCompletionPortThreads);
        var memory = GC.GetGCMemoryInfo();
        var process = Process.GetCurrentProcess();
        Emit(
          TextLogEventType.Sampled,
          "Runtime sample",
          new[]
          {
            new TextLogField("elapsedMs", _stopwatch.ElapsedMilliseconds),
            new TextLogField("allocBytes", GC.GetTotalAllocatedBytes(precise: false) - _allocatedBytesAtStart),
            new TextLogField("heapBytes", memory.HeapSizeBytes > 0
              ? memory.HeapSizeBytes
              : GC.GetTotalMemory(forceFullCollection: false)),
            new TextLogField("wsBytes", process.WorkingSet64),
            new TextLogField("gen0", GC.CollectionCount(0)),
            new TextLogField("gen1", GC.CollectionCount(1)),
            new TextLogField("gen2", GC.CollectionCount(2)),
            new TextLogField("tpThreads", ThreadPool.ThreadCount),
            new TextLogField("tpPending", ThreadPool.PendingWorkItemCount),
            new TextLogField("tpCompleted", ThreadPool.CompletedWorkItemCount),
            new TextLogField("availableWorkers", availableWorkerThreads),
            new TextLogField("maxWorkers", maximumWorkerThreads),
            new TextLogField("availableCompletionPorts", availableCompletionPortThreads),
            new TextLogField("maxCompletionPorts", maximumCompletionPortThreads)
          });
    }

    private IReadOnlyList<TextLogField> CreateTerminalFields(
        PrototypeAnalysisResult result,
        IReadOnlyList<ConcurrencyOperationTelemetry> operations,
        string status)
    {
        var fields = CreatePoolFields(operations).ToList();
        fields.Add(new TextLogField("status", status));
        fields.Add(new TextLogField("elapsedMs", _stopwatch.ElapsedMilliseconds));
        fields.Add(new TextLogField("seedMarks", result.SeedMarks.Count));
        fields.Add(new TextLogField("propagatedMarks", result.PropagatedMarks.Count));
        fields.Add(new TextLogField("liftedMarks", result.LiftedMarks.Count));
        fields.Add(new TextLogField("decisions", result.Decisions.Count));
        fields.Add(new TextLogField("edits", result.Edits.Count));
        fields.Add(new TextLogField("diags", result.Diagnostics?.Count ?? 0));
        fields.Add(new TextLogField("nodes", result.GraphMetrics?.NodeCount ?? 0));
        fields.Add(new TextLogField("edges", result.GraphMetrics?.EdgeCount ?? 0));
        fields.Add(new TextLogField("ruleGraphReadyPeak", result.RuleGraphMetrics?.PeakReadyNodeCount ?? 0));
        fields.Add(new TextLogField("ruleGraphConcurrentPeak", result.RuleGraphMetrics?.PeakConcurrentNodeCount ?? 0));
        return fields;
    }

    private static IReadOnlyList<TextLogField> CreatePoolFields(
        IReadOnlyList<ConcurrencyOperationTelemetry> operations)
    {
        return new[]
        {
          new TextLogField("poolOperations", operations.Count),
          new TextLogField("poolQueueWaitMs", operations.Sum(operation => operation.QueueWait.TotalMilliseconds)),
          new TextLogField("poolQueueWaitMaxMs", operations.Select(operation => operation.QueueWait.TotalMilliseconds).DefaultIfEmpty().Max()),
          new TextLogField("poolPeakActive", operations.Select(operation => operation.PeakActiveWorkItemCount).DefaultIfEmpty().Max()),
          new TextLogField("poolPeakReady", operations.Select(operation => operation.PeakReadyWorkItemCount).DefaultIfEmpty().Max()),
          new TextLogField("poolPeakBufferItems", operations.Select(operation => operation.PeakCompletedBufferItemCount).DefaultIfEmpty().Max()),
          new TextLogField("poolPeakRecords", operations.Select(operation => operation.PeakRetainedRecordCount).DefaultIfEmpty().Max()),
          new TextLogField("poolPeakReservedBytes", operations.Select(operation => operation.PeakReservedByteCount).DefaultIfEmpty().Max()),
          new TextLogField("poolWeightedTurns", operations.Count(operation => operation.AdmissionReason == ConcurrencyAdmissionReason.WeightedTurn)),
          new TextLogField("poolAgingPromotions", operations.Count(operation => operation.AdmissionReason == ConcurrencyAdmissionReason.AgingPromotion))
        };
    }

    private void EmitPoolOperations(IReadOnlyList<ConcurrencyOperationTelemetry> operations)
    {
        foreach (var operation in operations)
        {
            Emit(
              TextLogEventType.Summary,
              "Pool operation",
              new[]
              {
                new TextLogField("poolOperation", operation.OperationKind),
                new TextLogField("workType", operation.WorkType),
                new TextLogField("queueWaitMs", operation.QueueWait.TotalMilliseconds),
                new TextLogField("peakActive", operation.PeakActiveWorkItemCount),
                new TextLogField("peakReady", operation.PeakReadyWorkItemCount),
                new TextLogField("peakBufferItems", operation.PeakCompletedBufferItemCount),
                new TextLogField("peakRecords", operation.PeakRetainedRecordCount),
                new TextLogField("peakReservedBytes", operation.PeakReservedByteCount),
                new TextLogField("admissionReason", operation.AdmissionReason?.ToString()),
                new TextLogField("elapsedMs", operation.Elapsed.TotalMilliseconds),
                new TextLogField("failureDrainMs", operation.FailureDrainElapsed.TotalMilliseconds)
              },
              operation: "pool");
        }
    }

    private void Emit(
        TextLogEventType eventType,
        string message,
        IReadOnlyList<TextLogField> fields,
        string operation = "analysis")
    {
        try
        {
            _sink.Emit(new TextLogEvent(
              DateTimeOffset.UtcNow,
              TextLogLevel.Debug,
              TextLogCategory.Run,
              eventType,
              message,
              _runId,
              Operation: operation,
              InputKind: _inputKind,
              InputPath: _inputPath,
              Dop: _dop,
              Fields: fields));
        }
        catch
        {
            // Runtime metrics are diagnostic and cannot alter analysis semantics.
        }
    }
}
