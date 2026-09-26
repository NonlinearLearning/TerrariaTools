using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using NL.Concurrency;
using NLCPG.Builder;

namespace NLCPG.Builder.Concurrency;

/// <summary>
/// 配置 CPG WorkBatch 固定 worker 执行器的队列和准入边界。
/// </summary>
public sealed record CpgWorkBatchExecutorOptions
{
    public CpgWorkBatchExecutorOptions(
      int maxDegreeOfParallelism,
      int? queueCapacity = null,
      int? fragmentSinkCapacity = null,
      int? maxQueuedEstimatedCost = null,
      long? maxQueuedEstimatedBytes = null,
      ConcurrencyWorkType workType = ConcurrencyWorkType.Throughput,
      long admissionReservedByteCount = 0,
      Action<CpgWorkBatchPerformanceEvent>? telemetrySink = null,
      string? performanceRunId = null,
      bool useSynchronousExecution = false)
    {
        if (maxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
        }

        if (queueCapacity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }

        if (fragmentSinkCapacity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fragmentSinkCapacity));
        }

        if (maxQueuedEstimatedCost is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueuedEstimatedCost));
        }

        if (maxQueuedEstimatedBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueuedEstimatedBytes));
        }

        if (admissionReservedByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(admissionReservedByteCount));
        }

        MaxDegreeOfParallelism = maxDegreeOfParallelism;
        QueueCapacity = queueCapacity;
        FragmentSinkCapacity = fragmentSinkCapacity;
        MaxQueuedEstimatedCost = maxQueuedEstimatedCost;
        MaxQueuedEstimatedBytes = maxQueuedEstimatedBytes;
        WorkType = workType;
        AdmissionReservedByteCount = admissionReservedByteCount;
        TelemetrySink = telemetrySink;
        PerformanceRunId = performanceRunId;
        UseSynchronousExecution = useSynchronousExecution;
    }

    public int MaxDegreeOfParallelism { get; }

    public int? QueueCapacity { get; }

    public int? FragmentSinkCapacity { get; }

    public int? MaxQueuedEstimatedCost { get; }

    public long? MaxQueuedEstimatedBytes { get; }

    public ConcurrencyWorkType WorkType { get; }

    public long AdmissionReservedByteCount { get; }

    public Action<CpgWorkBatchPerformanceEvent>? TelemetrySink { get; }

    public string? PerformanceRunId { get; }

    public bool UseSynchronousExecution { get; }

    public int EffectiveQueueCapacity => QueueCapacity ?? Math.Max(2 * MaxDegreeOfParallelism, 8);

    public int EffectiveFragmentSinkCapacity => FragmentSinkCapacity ?? Math.Max(2 * MaxDegreeOfParallelism, 8);

    public int EffectiveMaxQueuedEstimatedCost => MaxQueuedEstimatedCost ?? int.MaxValue;

    public long EffectiveMaxQueuedEstimatedBytes => MaxQueuedEstimatedBytes ?? long.MaxValue;
}

public sealed record CpgWorkBatchResultMetrics(
  int OutputNodeCount,
  int OutputEdgeCount,
  int FragmentBytes)
{
    public static CpgWorkBatchResultMetrics Empty { get; } = new(0, 0, 0);
}

internal sealed class CpgWorkBatchExecutionTracker
{
    private readonly string _stageId;
    private readonly string? _runId;
    private readonly Action<CpgWorkBatchPerformanceEvent>? _telemetrySink;
    private readonly ConcurrentDictionary<int, CpgWorkBatchExecutionTrace> _traces = new();
    private int _queuedCount;
    private int _peakQueuedCount;
    private int _activeWorkerCount;
    private int _peakActiveWorkerCount;
    private int _completedNotReducedCount;
    private int _peakCompletedNotReducedCount;

    internal CpgWorkBatchExecutionTracker(
      string stageId,
      Action<CpgWorkBatchPerformanceEvent>? telemetrySink,
      string? runId)
    {
        _stageId = stageId;
        _telemetrySink = telemetrySink;
        _runId = runId;
    }

    internal void BatchEnqueueStarted(CpgWorkBatch batch)
    {
        _traces[batch.ShardOrder] = new CpgWorkBatchExecutionTrace(
          _stageId,
          batch,
          Stopwatch.GetTimestamp());
        UpdateMaximum(ref _peakQueuedCount, Interlocked.Increment(ref _queuedCount));
    }

    internal void BatchEnqueueFailed(CpgWorkBatch batch)
    {
        _traces.TryRemove(batch.ShardOrder, out _);
        Interlocked.Decrement(ref _queuedCount);
    }

    internal void BatchStarted(CpgWorkBatch batch, int workerIndex)
    {
        if (!_traces.TryGetValue(batch.ShardOrder, out var trace))
        {
            trace = new CpgWorkBatchExecutionTrace(_stageId, batch, Stopwatch.GetTimestamp());
            _traces[batch.ShardOrder] = trace;
        }

        trace.WorkerIndex = workerIndex;
        trace.QueueWaitMilliseconds = ToMilliseconds(Stopwatch.GetTimestamp() - trace.EnqueuedTimestamp);
        Interlocked.Decrement(ref _queuedCount);
        UpdateMaximum(ref _peakActiveWorkerCount, Interlocked.Increment(ref _activeWorkerCount));
        trace.StartedTimestamp = Stopwatch.GetTimestamp();
    }

    internal void BatchFailed(CpgWorkBatch batch, int workerIndex)
    {
        if (_traces.TryGetValue(batch.ShardOrder, out var trace))
        {
            trace.WorkerIndex = workerIndex;
        }

        Interlocked.Decrement(ref _activeWorkerCount);
        _traces.TryRemove(batch.ShardOrder, out _);
    }

    internal CpgWorkBatchExecutionTrace BatchCompleted(
      CpgWorkBatch batch,
      int workerIndex,
      CpgWorkBatchResultMetrics resultMetrics)
    {
        if (!_traces.TryGetValue(batch.ShardOrder, out var trace))
        {
            trace = new CpgWorkBatchExecutionTrace(_stageId, batch, Stopwatch.GetTimestamp());
        }

        trace.WorkerIndex = workerIndex;
        trace.ProcessingElapsedMilliseconds = ToMilliseconds(Stopwatch.GetTimestamp() - trace.StartedTimestamp);
        trace.OutputMetrics = resultMetrics;
        trace.CompletedTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Decrement(ref _activeWorkerCount);
        UpdateMaximum(
          ref _peakCompletedNotReducedCount,
          Interlocked.Increment(ref _completedNotReducedCount));
        return trace;
    }

    internal void Complete()
    {
        _traces.Clear();
    }

    internal void Publish(CpgWorkBatchExecutionTrace trace)
    {
        trace.ReducerWaitElapsedMilliseconds = ToMilliseconds(Stopwatch.GetTimestamp() - trace.CompletedTimestamp);
        Interlocked.Decrement(ref _completedNotReducedCount);
        var eventData = new CpgWorkBatchPerformanceEvent(
          trace.StageId,
          trace.Batch.BatchId,
          trace.Batch.ShardOrder,
          trace.Batch.Items.Count,
          trace.Batch.EstimatedCost,
          trace.Batch.EstimatedBytes,
          trace.OutputMetrics.OutputNodeCount,
          trace.OutputMetrics.OutputEdgeCount,
          trace.OutputMetrics.FragmentBytes,
          trace.QueueWaitMilliseconds,
          trace.ProcessingElapsedMilliseconds,
          trace.ReducerWaitElapsedMilliseconds,
          trace.WorkerIndex,
          Volatile.Read(ref _peakActiveWorkerCount),
          Volatile.Read(ref _peakQueuedCount),
          Volatile.Read(ref _peakCompletedNotReducedCount),
          _runId);
        try
        {
            _telemetrySink?.Invoke(eventData);
        }
        catch
        {
        }
        _traces.TryRemove(trace.Batch.ShardOrder, out _);
    }

    private static long ToMilliseconds(long timestampDelta)
    {
        return (long)(timestampDelta * 1000d / Stopwatch.Frequency);
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current || Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }
}

internal sealed class CpgWorkBatchExecutionTrace
{
    internal CpgWorkBatchExecutionTrace(string stageId, CpgWorkBatch batch, long enqueuedTimestamp)
    {
        StageId = stageId;
        Batch = batch;
        EnqueuedTimestamp = enqueuedTimestamp;
        StartedTimestamp = enqueuedTimestamp;
        CompletedTimestamp = Stopwatch.GetTimestamp();
    }

    internal string StageId { get; }
    internal CpgWorkBatch Batch { get; }
    internal long EnqueuedTimestamp { get; }
    internal long StartedTimestamp { get; set; }
    internal long CompletedTimestamp { get; set; }
    internal int WorkerIndex { get; set; }
    internal double QueueWaitMilliseconds { get; set; }
    internal long ProcessingElapsedMilliseconds { get; set; }
    internal long ReducerWaitElapsedMilliseconds { get; set; }
    internal CpgWorkBatchResultMetrics OutputMetrics { get; set; } = CpgWorkBatchResultMetrics.Empty;

}

/// <summary>
/// 通过有界 batch channel 和固定 worker 循环执行 CPG 工作。
/// </summary>
public sealed class CpgWorkBatchExecutor
{
    private readonly CpgWorkBatchExecutorOptions _options;
    private readonly IConcurrencyPool _concurrencyPool;
    private readonly Action? _workerComputeWindowEnter;
    private readonly Action? _workerComputeWindowExit;
    private readonly Action<string>? _stageExecutionObserved;
    private readonly Action? _sharedStateWindowEnter;
    private readonly Action? _sharedStateWindowExit;

    public CpgWorkBatchExecutor(
      CpgWorkBatchExecutorOptions options,
      IConcurrencyPool? concurrencyPool = null,
      Action? workerComputeWindowEnter = null,
      Action? workerComputeWindowExit = null,
      Action<string>? stageExecutionObserved = null,
      Action? sharedStateWindowEnter = null,
      Action? sharedStateWindowExit = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _concurrencyPool = concurrencyPool ?? new BoundedConcurrencyPool();
        _workerComputeWindowEnter = workerComputeWindowEnter;
        _workerComputeWindowExit = workerComputeWindowExit;
        _stageExecutionObserved = stageExecutionObserved;
        _sharedStateWindowEnter = sharedStateWindowEnter;
        _sharedStateWindowExit = sharedStateWindowExit;
    }

    /// <summary>
    /// 通知观察者"某阶段确实走了本执行器"。
    /// <para>
    /// G0-P **R-4**：<c>StageQuotaPolicy.BatchPlanCapableStages</c> 声称"这 8 个阶段存在批次型
    /// plan（即走 <c>_workBatchExecutor</c>）"，但**此前无人对账**——该表若漏了某个阶段，
    /// 它会被 <c>Resolve</c> 归入 <see cref="StageQuotaBasis.NoBatchPlan"/>（"R-3/R-4 对它是
    /// N/A"），从而**静默豁免**整套配额作用域治理，包括未标定 <c>Dedicated</c> 的拒绝守卫。
    /// 这与附录 AA 的形态同源：**声明在，机制不在**。
    /// </para>
    /// <para>
    /// 观察点在 <see cref="ExecuteWithCollectionModeAsync{TResult}"/>——三个公开入口
    /// （<c>ExecuteFragmentsAsync</c>/<c>ExecuteAsync</c>/<c>ExecuteAndConsumeAsync</c>）
    /// 与同步/异步两条路径的**唯一公共祖先**，故新增入口无法绕过观察。
    /// </para>
    /// <para>
    /// ⚠ 刻意在**调用线程**上、worker 启动**之前**触发：观察者（builder）用普通集合记账，
    /// 若放到 worker 内会引入新的并发写入面。
    /// </para>
    /// </summary>
    private void NotifyStageExecutionObserved(string? stageId)
    {
        _stageExecutionObserved?.Invoke(stageId ?? "CPG.WorkBatch");
    }

    /// <summary>
    /// 在 worker 内执行 <paramref name="processBatch"/>，并按需把调用包进 L1 计算窗口。
    /// <para>
    /// G0-P 附录 R.3：L1 只允许产出 fragment，不得写共享图。把守卫**放在本方法**
    /// 而不是逐个 pass 接线，是因为这里是全部 8 个阶段的唯一公共通道——
    /// 逐阶段接线会让"新阶段忘了加守卫"退化成又一处"声明代替机制"（附录 AB）。
    /// </para>
    /// <para>
    /// ⚠ 窗口用 <c>try/finally</c> 闭合：worker 内抛异常若不退出窗口，
    /// 同一线程随后被复用的合法构图会被误报。
    /// </para>
    /// </summary>
    private static TResult ProcessBatchInWorkerWindow<TResult>(
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      CpgWorkBatch batch,
      int workerIndex,
      CancellationToken cancellationToken,
      Action? windowEnter,
      Action? windowExit)
    {
        if (windowEnter is null)
        {
            return processBatch(batch, workerIndex, cancellationToken);
        }

        windowEnter();
        try
        {
            return processBatch(batch, workerIndex, cancellationToken);
        }
        finally
        {
            windowExit?.Invoke();
        }
    }

    /// <summary>
    /// 把图侧（L1 不写共享图）与 builder 侧（L1 不共享可变中间态）两对钩子合成一对。
    /// <para>
    /// 合并而非并列，是为了让 <see cref="ProcessBatchInWorkerWindow{TResult}"/> 保持
    /// "一对 enter/exit" 的单一形态：窗口的**开合点**只有一个，新增的守卫种类不可能
    /// 因为"忘了在另一条路径上也开窗"而被漏掉。两对都为 <c>null</c> 时返回 <c>null</c>，
    /// 从而保留"无钩子即无开销"的既有快路径。
    /// </para>
    /// <para>
    /// 退出按**相反顺序**，与嵌套 <c>using</c> 的语义一致。
    /// </para>
    /// </summary>
    private static Action? CombineWindows(Action? first, Action? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return () =>
        {
            first();
            second();
        };
    }

    private static Action? CombineWindowsReversed(Action? first, Action? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return () =>
        {
            second();
            first();
        };
    }

    /// <summary>
    /// 获取本执行器为一次有足够输入的 build 启动的固定 worker 数量。
    /// </summary>
    public int WorkerCount => _options.MaxDegreeOfParallelism;

    /// <summary>
    /// 执行 batch，并按 stable order 将 fragment 交给 reducer。
    /// </summary>
    /// <param name="batches">待处理的不可变 batch 集合。</param>
    /// <param name="processBatch">在 worker 内处理一个 batch 的函数。</param>
    /// <param name="reduceFragment">按 stable order 消费 fragment 的可选回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按 stable order 排列的 fragment。</returns>
    public Task<IReadOnlyList<LocalCpgFragment>> ExecuteFragmentsAsync(
      IReadOnlyList<CpgWorkBatch> batches,
      Func<CpgWorkBatch, int, CancellationToken, LocalCpgFragment> processBatch,
      Action<LocalCpgFragment>? reduceFragment = null,
      CancellationToken cancellationToken = default,
      string? stageId = null)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(processBatch);

        return ExecuteAsync<LocalCpgFragment>(
          batches,
          (batch, workerIndex, token) =>
          {
              var fragment = processBatch(batch, workerIndex, token);
              if (fragment.StableOrder != batch.StableOrder)
              {
                  throw new InvalidOperationException(
                    $"Fragment stable order {fragment.StableOrder} does not match batch {batch.StableOrder}.");
              }

              return fragment;
          },
          reduceFragment,
          cancellationToken,
          stageId,
          fragment => new CpgWorkBatchResultMetrics(
            fragment.Nodes.Count,
            fragment.Edges.Count,
            fragment.Metrics.FragmentBytes));
    }

    /// <summary>
    /// 执行 batch 并返回任意不可变事实结果；结果按 batch stable order 归并。
    /// </summary>
    public Task<IReadOnlyList<TResult>> ExecuteAsync<TResult>(
      IReadOnlyList<CpgWorkBatch> batches,
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      Action<TResult>? reduceResult = null,
      CancellationToken cancellationToken = default,
      string? stageId = null,
      Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(processBatch);

        return ExecuteWithCollectionModeAsync(
          batches,
          processBatch,
          reduceResult,
          collectResults: true,
          cancellationToken,
          stageId,
          resultMetrics);
    }

    /// <summary>
    /// 执行 batch 并按 stable order 把每个结果交给消费回调，执行器不累计保留结果。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ExecuteAsync{TResult}"/> 共用同一执行内核、worker 池、channel、准入与异常处理，
    /// 只去掉“已提交结果列表”这一长期强引用。乱序 pending、worker 当前值和 channel 仍可能临时持有结果。
    /// </remarks>
    /// <param name="batches">待处理的不可变 batch 集合。</param>
    /// <param name="processBatch">在 worker 内处理一个 batch 的函数。</param>
    /// <param name="consumeResult">按 stable order 消费结果的回调；必须提供。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="stageId">telemetry 阶段标识。</param>
    /// <param name="resultMetrics">在结果被消费之前按既有时机计算 telemetry 度量。</param>
    /// <returns>所有 batch 均已按 stable order 消费后完成的 Task。</returns>
    public Task ExecuteAndConsumeAsync<TResult>(
      IReadOnlyList<CpgWorkBatch> batches,
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      Action<TResult> consumeResult,
      CancellationToken cancellationToken = default,
      string? stageId = null,
      Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics = null)
    {
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(processBatch);
        ArgumentNullException.ThrowIfNull(consumeResult);

        return ExecuteWithCollectionModeAsync(
          batches,
          processBatch,
          consumeResult,
          collectResults: false,
          cancellationToken,
          stageId,
          resultMetrics);
    }

    private Task<IReadOnlyList<TResult>> ExecuteWithCollectionModeAsync<TResult>(
      IReadOnlyList<CpgWorkBatch> batches,
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      Action<TResult>? reduceResult,
      bool collectResults,
      CancellationToken cancellationToken,
      string? stageId,
      Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics)
    {
        // 三个公开入口与同步/异步两条路径的唯一公共祖先：观察"本阶段确实提交了批次"。
        NotifyStageExecutionObserved(stageId);

        if (_options.UseSynchronousExecution)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteSynchronously(
              batches,
              processBatch,
              reduceResult,
              collectResults,
              cancellationToken,
              stageId,
              resultMetrics));
        }

        return _concurrencyPool.ExecuteWithAdmissionAsync(
          new ConcurrencyAdmissionRequest(
            _options.WorkType,
            ReservedItemCount: 1,
            ReservedByteCount: _options.AdmissionReservedByteCount),
          token => ExecuteCoreAsync(
            batches,
            processBatch,
            reduceResult,
            collectResults,
            token,
            stageId,
            resultMetrics),
          cancellationToken);
    }

    private async Task<IReadOnlyList<TResult>> ExecuteCoreAsync<TResult>(
      IReadOnlyList<CpgWorkBatch> batches,
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      Action<TResult>? reduceResult,
      bool collectResults,
      CancellationToken cancellationToken,
      string? stageId,
      Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics)
    {
        if (batches.Count == 0)
        {
            return Array.Empty<TResult>();
        }

        // 归并与唯一性校验一律以 **ShardOrder**（跨文件全局单调）为准，而非 StableOrder
        // （后者是文件内局部序号，跨文件装箱时必然出现重复）。单文件时二者相等，故行为逐字不变。
        var orderedShardOrders = batches
          .Select(batch => batch.ShardOrder)
          .OrderBy(order => order)
          .ToArray();
        if (orderedShardOrders.Distinct().Count() != orderedShardOrders.Length)
        {
            throw new ArgumentException("WorkBatch shard orders must be unique.", nameof(batches));
        }

        var workerCount = _options.MaxDegreeOfParallelism;
        var batchChannel = Channel.CreateBounded<CpgWorkBatch>(new BoundedChannelOptions(_options.EffectiveQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });
        var resultChannel = Channel.CreateBounded<CpgBatchResult<TResult>>(new BoundedChannelOptions(_options.EffectiveFragmentSinkCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        });
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var queueBudget = new CpgWorkBatchQueueBudget(
          _options.EffectiveMaxQueuedEstimatedCost,
          _options.EffectiveMaxQueuedEstimatedBytes);
        var telemetry = new CpgWorkBatchExecutionTracker(
          stageId ?? "CPG.WorkBatch",
          _options.TelemetrySink,
          _options.PerformanceRunId);
        ExceptionDispatchInfo? firstFailure = null;
        var failureGate = new object();

        void RecordFailure(Exception exception)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return;
            }

            lock (failureGate)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }

            linkedCancellation.Cancel();
            batchChannel.Writer.TryComplete(exception);
            resultChannel.Writer.TryComplete(exception);
        }

        var reducerTask = ReduceFragmentsAsync(
          resultChannel.Reader,
          orderedShardOrders,
          reduceResult,
          collectResults,
          RecordFailure,
          telemetry,
          linkedCancellation.Token,
          // G0-P R.3 L1：归并回调运行在**独立任务**上，与本阶段的 worker 循环并发。
          // 若不在此开窗，归并线程窗口深度恒为 0，"归并写共享状态"将完全逃过守卫。
          // ⚠ 刻意**只**开共享态窗口，**不**开图侧窗口：归并层（L2）本就是唯一的图写入者，
          //   开图侧窗口会让它的合法构图被误判为 L1 违规。
          _sharedStateWindowEnter,
          _sharedStateWindowExit);
        // 合并两对钩子**一次**，供全部 worker 复用：既避免每个 worker 各分配一对闭包，
        // 也让"窗口的开合点只有一个"成为结构事实（而非依赖各处重复写对）。
        var workerWindowEnter = CombineWindows(_workerComputeWindowEnter, _sharedStateWindowEnter);
        var workerWindowExit = CombineWindowsReversed(_workerComputeWindowExit, _sharedStateWindowExit);
        var workers = Enumerable.Range(0, workerCount)
          .Select(workerIndex => Task.Run(
            () => WorkerLoopAsync(
              workerIndex,
              batchChannel.Reader,
              resultChannel.Writer,
              queueBudget,
              processBatch,
              RecordFailure,
              telemetry,
              resultMetrics,
              linkedCancellation.Token,
              // G0-P R.3 L1：把 worker 调用包进计算窗口（钩子为 null 时无开销）。
              // 图侧（不写共享图）与 builder 侧（不共享可变中间态）两对钩子已在上方合并为
              // 同一对开合点，故不存在"只开了其中一个窗口"的漏配形态。
              workerWindowEnter,
              workerWindowExit),
            CancellationToken.None))
          .ToArray();

        try
        {
            foreach (var batch in batches)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                await queueBudget.AcquireAsync(batch, linkedCancellation.Token).ConfigureAwait(false);
                try
                {
                    telemetry.BatchEnqueueStarted(batch);
                    await batchChannel.Writer.WriteAsync(batch, linkedCancellation.Token).ConfigureAwait(false);
                }
                catch
                {
                    telemetry.BatchEnqueueFailed(batch);
                    queueBudget.Release(batch);
                    throw;
                }
            }

            batchChannel.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            batchChannel.Writer.TryComplete(exception);
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            resultChannel.Writer.TryComplete(firstFailure?.SourceException);
        }

        try
        {
            var fragments = await reducerTask.ConfigureAwait(false);
            if (firstFailure is not null)
            {
                firstFailure.Throw();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return fragments;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            if (firstFailure is not null)
            {
                firstFailure.Throw();
            }

            throw;
        }
        finally
        {
            linkedCancellation.Cancel();
            batchChannel.Writer.TryComplete();
            resultChannel.Writer.TryComplete(firstFailure?.SourceException);
            linkedCancellation.Dispose();
            telemetry.Complete();
        }
    }

    private IReadOnlyList<TResult> ExecuteSynchronously<TResult>(
      IReadOnlyList<CpgWorkBatch> batches,
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      Action<TResult>? reduceResult,
      bool collectResults,
      CancellationToken cancellationToken,
      string? stageId,
      Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics)
    {
        var telemetry = new CpgWorkBatchExecutionTracker(
          stageId ?? "CPG.WorkBatch",
          _options.TelemetrySink,
          _options.PerformanceRunId);
        List<TResult>? collected = collectResults ? new List<TResult>(batches.Count) : null;
        // 与异步路径同一合并方式：两对钩子共用一个开合点。在循环**外**合并一次，
        // 避免每个批次各分配一对闭包（同步路径无并发，钩子仅用于统一守卫语义）。
        var workerWindowEnter = CombineWindows(_workerComputeWindowEnter, _sharedStateWindowEnter);
        var workerWindowExit = CombineWindowsReversed(_workerComputeWindowExit, _sharedStateWindowExit);
        foreach (var batch in batches.OrderBy(batch => batch.ShardOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            telemetry.BatchEnqueueStarted(batch);
            telemetry.BatchStarted(batch, workerIndex: 0);
            TResult result;
            try
            {
                result = ProcessBatchInWorkerWindow(
                  processBatch,
                  batch,
                  0,
                  cancellationToken,
                  workerWindowEnter,
                  workerWindowExit);
            }
            catch (Exception exception)
            {
                telemetry.BatchFailed(batch, 0);
                throw new InvalidOperationException(
                  $"Synchronous WorkBatch execution failed for batch {batch.BatchId}.",
                  exception);
            }

            var trace = telemetry.BatchCompleted(
              batch,
              0,
              resultMetrics?.Invoke(result) ?? CpgWorkBatchResultMetrics.Empty);
            reduceResult?.Invoke(result);
            collected?.Add(result);
            telemetry.Publish(trace);
        }

        telemetry.Complete();
        return collected ?? (IReadOnlyList<TResult>)Array.Empty<TResult>();
    }

    private static async Task WorkerLoopAsync<TResult>(
      int workerIndex,
      ChannelReader<CpgWorkBatch> batchReader,
      ChannelWriter<CpgBatchResult<TResult>> resultWriter,
      CpgWorkBatchQueueBudget queueBudget,
      Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
      Action<Exception> recordFailure,
      CpgWorkBatchExecutionTracker telemetry,
      Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics,
      CancellationToken cancellationToken,
      Action? windowEnter,
      Action? windowExit)
    {
        try
        {
            await foreach (var batch in batchReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                queueBudget.Release(batch);
                cancellationToken.ThrowIfCancellationRequested();
                TResult result;
                telemetry.BatchStarted(batch, workerIndex);
                try
                {
                    result = ProcessBatchInWorkerWindow(
                      processBatch,
                      batch,
                      workerIndex,
                      cancellationToken,
                      windowEnter,
                      windowExit);
                }
                catch (Exception exception)
                {
                    telemetry.BatchFailed(batch, workerIndex);
                    recordFailure(exception);
                    return;
                }

                try
                {
                    await resultWriter.WriteAsync(
                      new CpgBatchResult<TResult>(
                        batch.ShardOrder,
                        result,
                        telemetry.BatchCompleted(
                          batch,
                          workerIndex,
                          resultMetrics?.Invoke(result) ?? CpgWorkBatchResultMetrics.Empty)),
                      cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    recordFailure(exception);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            recordFailure(exception);
        }
    }

    private static async Task<IReadOnlyList<TResult>> ReduceFragmentsAsync<TResult>(
      ChannelReader<CpgBatchResult<TResult>> resultReader,
      IReadOnlyList<int> orderedShardOrders,
      Action<TResult>? reduceResult,
      bool collectResults,
      Action<Exception> recordFailure,
      CpgWorkBatchExecutionTracker telemetry,
      CancellationToken cancellationToken,
      Action? sharedStateWindowEnter = null,
      Action? sharedStateWindowExit = null)
    {
        // 归并窗口：与 worker 窗口同一守卫、不同触发线程。
        // 用 try/finally 闭合，归并回调抛异常时也必须退出——否则该线程被复用后，
        // 后续合法写入会被误报为"窗口内无门写入"。
        sharedStateWindowEnter?.Invoke();
        try
        {
            return await ReduceFragmentsCoreAsync(
              resultReader,
              orderedShardOrders,
              reduceResult,
              collectResults,
              recordFailure,
              telemetry,
              cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sharedStateWindowExit?.Invoke();
        }
    }

    private static async Task<IReadOnlyList<TResult>> ReduceFragmentsCoreAsync<TResult>(
      ChannelReader<CpgBatchResult<TResult>> resultReader,
      IReadOnlyList<int> orderedShardOrders,
      Action<TResult>? reduceResult,
      bool collectResults,
      Action<Exception> recordFailure,
      CpgWorkBatchExecutionTracker telemetry,
      CancellationToken cancellationToken)
    {
        var pending = new Dictionary<int, CpgBatchResult<TResult>>();
        // 消费入口不建立与批次数等长的结果列表，只按 stable order 把结果交给回调。
        List<TResult>? collected = collectResults ? new List<TResult>(orderedShardOrders.Count) : null;
        var nextOrderIndex = 0;
        try
        {
            await foreach (var result in resultReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                pending.Add(result.ShardOrder, result);
                while (nextOrderIndex < orderedShardOrders.Count &&
                       pending.Remove(orderedShardOrders[nextOrderIndex], out var nextResult))
                {
                    reduceResult?.Invoke(nextResult.Result);
                    collected?.Add(nextResult.Result);
                    telemetry.Publish(nextResult.Telemetry);
                    nextOrderIndex++;
                }
            }

            if (nextOrderIndex != orderedShardOrders.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The fragment sink completed before all batches were reduced.");
            }

            return collected ?? (IReadOnlyList<TResult>)Array.Empty<TResult>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            recordFailure(exception);
            throw;
        }
    }

    private sealed record CpgBatchResult<TResult>(
      int ShardOrder,
      TResult Result,
      CpgWorkBatchExecutionTrace Telemetry);
}

internal sealed class CpgWorkBatchQueueBudget
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly int _maxCost;
    private readonly long _maxBytes;
    private int _queuedCost;
    private long _queuedBytes;

    public CpgWorkBatchQueueBudget(int maxCost, long maxBytes)
    {
        _maxCost = maxCost;
        _maxBytes = maxBytes;
    }

    public async ValueTask AcquireAsync(CpgWorkBatch batch, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                var fits = _queuedCost <= _maxCost - batch.EstimatedCost &&
                  _queuedBytes <= _maxBytes - batch.EstimatedBytes;
                var isEmpty = _queuedCost == 0 && _queuedBytes == 0;
                if (fits || isEmpty)
                {
                    _queuedCost = checked(_queuedCost + batch.EstimatedCost);
                    _queuedBytes = checked(_queuedBytes + batch.EstimatedBytes);
                    return;
                }
            }

            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Release(CpgWorkBatch batch)
    {
        lock (_gate)
        {
            _queuedCost -= batch.EstimatedCost;
            _queuedBytes -= batch.EstimatedBytes;
            if (_queuedCost < 0 || _queuedBytes < 0)
            {
                throw new InvalidOperationException("WorkBatch queue budget was released more than once.");
            }
        }

        _available.Release();
    }
}
