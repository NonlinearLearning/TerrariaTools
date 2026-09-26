using System.Diagnostics;
using System.Globalization;
using NL.Concurrency;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;
using NLISSN.Logging;

namespace NLISSN.Telemetry;

/// <summary>
/// Writes opt-in process and pool metrics for one configuration-driven analysis without affecting scheduling.
/// </summary>
internal sealed class RuntimeMeasurementLog : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private readonly TextLogFileSink _sink;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly CancellationTokenSource _samplingCancellation = new();
    private readonly Task _samplingTask;
    private readonly long _allocatedBytesAtStart;
    private readonly string _runId;
    private readonly string? _inputPath;
    private readonly string _inputKind;
    private readonly IReadOnlyList<TextLogField> _executionFields;
    private int _completed;

    private RuntimeMeasurementLog(
        string path,
        LoggingSettings logging,
        string runId,
        string? inputPath,
        AnalysisRuntime runtime)
    {
        _sink = TextLogFileSink.Create(
          path,
          new TextLogFormatter(),
          TextLogFilter.CreateRuntimeFilter(logging.Profile, logging.Level, logging.Categories, logging.Events, logging.View));
        _runId = string.IsNullOrWhiteSpace(runId) ? "unassigned" : runId;
        _inputPath = inputPath;
        _inputKind = inputPath is not null && Directory.Exists(inputPath) ? "directory" : "file";
        _executionFields = CreateExecutionFields(runtime);
        _allocatedBytesAtStart = GC.GetTotalAllocatedBytes(precise: false);
        Emit(TextLogEventType.Started, "Analysis started", Array.Empty<TextLogField>());
        EmitSample();
        _samplingTask = SampleUntilCompletedAsync();
    }

    /// <summary>
    /// 打印**内核实际生效**的类别上限与 worker 数，而非 YAML 中请求的值。
    /// 风险 R4/R4b：`groupParallelism: false`（默认）时规则组实际为 1，
    /// 若只打印请求值会让运行日志与真实调度不符。
    /// </summary>
    private static IReadOnlyList<TextLogField> CreateExecutionFields(AnalysisRuntime runtime)
    {
        var options = runtime.ExecutionOptions;
        var schedulerOptions = runtime.SchedulerOptions;
        return new[]
        {
          new TextLogField("directoryDop", options.EffectiveDirectoryMaxDegreeOfParallelism),
          new TextLogField("cpgDop", options.EffectiveCpgMaxDegreeOfParallelism),
          new TextLogField("groupDop", options.EffectiveGroupMaxDegreeOfParallelism),
          new TextLogField("helperDop", options.EffectiveHelperMaxDegreeOfParallelism),
          new TextLogField("replayDop", options.EffectiveReplayMaxDegreeOfParallelism),
          new TextLogField("maxConcurrentOperations", options.EffectiveMaxConcurrentOperations),
          new TextLogField("workerCount", schedulerOptions.WorkerCount),
          new TextLogField("ruleGroupEffective", schedulerOptions.ResolveLimit(WorkCategories.RuleGroup)),
          new TextLogField("helperEffective", schedulerOptions.ResolveLimit(WorkCategories.Helper)),
          new TextLogField("directoryEffective", schedulerOptions.ResolveLimit(WorkCategories.Directory)),
          new TextLogField("cpgEffective", schedulerOptions.ResolveLimit(WorkCategories.Cpg)),
          new TextLogField("replayEffective", schedulerOptions.ResolveLimit(WorkCategories.Replay)),
          new TextLogField("defaultEffective", schedulerOptions.ResolveLimit(WorkCategories.Default)),
          new TextLogField("groupParallelism", schedulerOptions.GroupParallelism),
          new TextLogField("directoryParallelism", schedulerOptions.DirectoryParallelism),
          new TextLogField("helperParallelism", schedulerOptions.HelperParallelism),
          new TextLogField(
            "directoryWindowSemantics",
            "directoryEffective limits concurrently live files per window, not a pool DOP")
        };
    }

    public static RuntimeMeasurementLog? TryCreate(
        string? path,
        LoggingSettings logging,
        string runId,
        string? inputPath,
        AnalysisRuntime runtime)
    {
        return path is null ? null : new RuntimeMeasurementLog(path, logging, runId, inputPath, runtime);
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
        EmitWorkerUtilization(runtime);
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
        EmitWorkerUtilization(runtime);
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

    /// <summary>
    /// 逐条写出**每个长期 worker** 的忙碌率，而不是只给一个汇总数。
    /// </summary>
    /// <remarks>
    /// 用 <c>op=worker</c> 与 <c>pool</c> 区分，使消费者能把它们分开聚合。
    /// worker 未启动（下标项为 null）时**显式写 skipped**，避免「没有行」被读成「0% 使用率」。
    /// </remarks>
    private void EmitWorkerUtilization(AnalysisRuntime runtime)
    {
        IReadOnlyList<WorkerUtilization?> snapshot;
        try
        {
            snapshot = runtime.Scheduler.GetWorkerUtilization();
        }
        catch
        {
            // 与其它遥测一致：诊断失败不得改变运行语义。
            return;
        }

        for (var index = 0; index < snapshot.Count; index++)
        {
            var worker = snapshot[index];
            if (worker is null)
            {
                Emit(
                  TextLogEventType.Summary,
                  "Worker utilization",
                  new[]
                  {
                      new TextLogField("workerIndex", index),
                      new TextLogField("status", "never-started"),
                  },
                  operation: "worker");
                continue;
            }

            Emit(
              TextLogEventType.Summary,
              "Worker utilization",
              new[]
              {
                  new TextLogField("workerIndex", worker.WorkerIndex),
                  new TextLogField("items", worker.ExecutedItemCount),
                  new TextLogField("busyMs", worker.BusyTime.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)),
                  new TextLogField("idleMs", worker.IdleTime.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)),
                  new TextLogField("lifetimeMs", worker.Lifetime.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)),
                  new TextLogField("utilization", worker.UtilizationRatio.ToString("F4", CultureInfo.InvariantCulture)),
              },
              operation: "worker");
        }
    }

    private void Emit(
        TextLogEventType eventType,
        string message,
        IReadOnlyList<TextLogField> fields,
        string operation = "analysis")    {
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
              Fields: _executionFields.Concat(fields).ToArray()));
        }
        catch
        {
            // Runtime metrics are diagnostic and cannot alter analysis semantics.
        }
    }
}
