using System.Collections.Concurrent;
using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// 覆盖消费入口 <see cref="CpgWorkBatchExecutor.ExecuteAndConsumeAsync{TResult}"/> 的
/// stable-order 契约、取消/失败传播，以及“不再累计保留已消费结果”的生命周期差异。
/// </summary>
public sealed class CpgWorkBatchConsumptionTests
{
    /// <summary>
    /// 用可控事件（而非 sleep）制造完成乱序：DOP2 下前两个 batch 同时进入不同 worker，
    /// 放行顺序为 1 → 0，即完成顺序与 stable order 相反；消费顺序仍必须为 stable order。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenCompletionOrderIsReversed_ConsumesInStableOrder()
    {
        const int batchCount = 4;
        const int gatedBatchCount = 2;
        var consumedOrders = new ConcurrentQueue<int>();
        var completionOrders = new ConcurrentQueue<int>();
        var completed = new int[gatedBatchCount];
        var gates = Enumerable.Range(0, gatedBatchCount)
          .Select(_ => new ManualResetEventSlim())
          .ToArray();
        using var enteredBoth = new CountdownEvent(gatedBatchCount);
        var executor = CreateExecutor(gatedBatchCount);
        Task? execution = null;

        try
        {
            execution = executor.ExecuteAndConsumeAsync(
              CreateBatches(batchCount),
              (batch, _, token) =>
              {
                  if (batch.StableOrder < gatedBatchCount)
                  {
                      enteredBoth.Signal();
                      gates[batch.StableOrder].Wait(token);
                  }

                  // 先记录完成顺序再置位：观察到 completed 即表示顺序已落定。
                  completionOrders.Enqueue(batch.StableOrder);
                  if (batch.StableOrder < gatedBatchCount)
                  {
                      Volatile.Write(ref completed[batch.StableOrder], 1);
                  }

                  return batch.StableOrder;
              },
              order => consumedOrders.Enqueue(order));

            Assert.True(
              enteredBoth.Wait(TimeSpan.FromSeconds(15)),
              "Batches 0 and 1 did not both enter a worker; the completion barrier was never established.");

            // 先放行 1、再放行 0：完成顺序与 stable order 相反。
            gates[1].Set();
            WaitForCompletion(completed, 1, TimeSpan.FromSeconds(15));
            gates[0].Set();
            WaitForCompletion(completed, 0, TimeSpan.FromSeconds(15));

            await execution.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            // 屏障超时也必须放行全部 worker，避免挂住测试进程。
            foreach (var gate in gates)
            {
                gate.Set();
            }

            if (execution is not null)
            {
                await IgnoreOutcomeAsync(execution);
            }

            foreach (var gate in gates)
            {
                gate.Dispose();
            }
        }

        var completionList = completionOrders.ToArray();
        Assert.True(
          Array.IndexOf(completionList, 1) < Array.IndexOf(completionList, 0),
          $"Completions were not out of order: [{string.Join(",", completionList)}].");
        Assert.Equal(Enumerable.Range(0, batchCount), consumedOrders.ToArray());
    }

    /// <summary>
    /// 新入口与旧收集入口在同一输入下的消费顺序必须一致，telemetry 的
    /// 载荷无关字段集合与事件数量也必须一致。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_MatchesExecuteAsyncOrderAndTelemetry()
    {
        const int batchCount = 9;
        const string stageId = "CPG.WorkBatch.ConsumptionContract";
        var collectedOrders = new List<int>();
        var consumedOrders = new List<int>();
        var collectedEvents = new ConcurrentQueue<CpgWorkBatchPerformanceEvent>();
        var consumedEvents = new ConcurrentQueue<CpgWorkBatchPerformanceEvent>();

        var collecting = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 4,
          fragmentSinkCapacity: 4,
          telemetrySink: collectedEvents.Enqueue));
        var consuming = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 4,
          fragmentSinkCapacity: 4,
          telemetrySink: consumedEvents.Enqueue));

        var batches = CreateBatches(batchCount);
        var collected = await collecting.ExecuteAsync(
          batches,
          (batch, _, _) => batch.StableOrder,
          collectedOrders.Add,
          stageId: stageId);

        await consuming.ExecuteAndConsumeAsync(
          batches,
          (batch, _, _) => batch.StableOrder,
          consumedOrders.Add,
          stageId: stageId);

        Assert.Equal(collected, collectedOrders);
        Assert.Equal(collectedOrders, consumedOrders);
        Assert.Equal(batchCount, consumedEvents.Count);

        // WorkerIndex / 队列水位依赖调度时序，不属于可比判据；只比较载荷无关字段。
        var expectedEvents = collectedEvents
          .Select(DescribeLoadIndependentEvent)
          .OrderBy(text => text, StringComparer.Ordinal)
          .ToArray();
        var actualEvents = consumedEvents
          .Select(DescribeLoadIndependentEvent)
          .OrderBy(text => text, StringComparer.Ordinal)
          .ToArray();
        Assert.Equal(expectedEvents, actualEvents);
        Assert.All(consumedEvents, performanceEvent =>
        {
            Assert.Equal(stageId, performanceEvent.StageId);
            Assert.True(performanceEvent.PeakActiveWorkerCount >= 1);
            Assert.True(performanceEvent.QueueHighWaterMark >= 1);
            Assert.True(performanceEvent.CompletedNotReducedHighWaterMark >= 1);
        });
    }

    /// <summary>同步适配路径也必须按 stable order 消费，且不返回结果列表。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenSynchronousExecution_ConsumesInStableOrder()
    {
        var consumedOrders = new List<int>();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 1,
          useSynchronousExecution: true));

        await executor.ExecuteAndConsumeAsync(
          CreateBatches(5).Reverse().ToArray(),
          (batch, _, _) => batch.StableOrder,
          consumedOrders.Add);

        Assert.Equal(Enumerable.Range(0, 5), consumedOrders);
    }

    /// <summary>空批次集合必须直接完成，且不得调用消费回调。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WithEmptyBatches_CompletesWithoutConsuming()
    {
        var consumed = 0;
        var executor = CreateExecutor(2);

        await executor.ExecuteAndConsumeAsync<int>(
          Array.Empty<CpgWorkBatch>(),
          (_, _, _) => 1,
          _ => Interlocked.Increment(ref consumed));

        Assert.Equal(0, consumed);
    }

    /// <summary>消费回调必须提供；缺失时在执行任何 batch 之前失败。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WithNullConsumer_ThrowsArgumentNullException()
    {
        var processed = 0;
        var executor = CreateExecutor(2);

        await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAndConsumeAsync<int>(
          CreateBatches(4),
          (_, _, _) =>
          {
              Interlocked.Increment(ref processed);
              return 1;
          },
          consumeResult: null!));

        Assert.Equal(0, processed);
    }

    /// <summary>入队前已取消：与旧入口一致，不处理任何 batch、不返回部分成功。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenCanceledBeforeEnqueue_DoesNotProcessOrConsume()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var processed = 0;
        var consumed = 0;
        var executor = CreateExecutor(2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAndConsumeAsync<int>(
          CreateBatches(8),
          (_, _, _) =>
          {
              Interlocked.Increment(ref processed);
              return 1;
          },
          _ => Interlocked.Increment(ref consumed),
          cancellation.Token));

        Assert.Equal(0, processed);
        Assert.Equal(0, consumed);
    }

    /// <summary>分析中取消：停止接受新 batch，并以取消异常收尾而非部分成功。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenCanceledDuringAnalysis_ThrowsInsteadOfReturningPartialSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        var processed = 0;
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2,
          queueCapacity: 1,
          fragmentSinkCapacity: 1));

        var execution = executor.ExecuteAndConsumeAsync(
          CreateBatches(12),
          (batch, _, token) =>
          {
              Interlocked.Increment(ref processed);
              entered.Set();
              while (!token.IsCancellationRequested)
              {
                  token.WaitHandle.WaitOne(50);
              }

              token.ThrowIfCancellationRequested();
              return batch.StableOrder;
          },
          _ => { },
          cancellation.Token);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "No worker entered a batch in time.");
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(Volatile.Read(ref processed) < 12);
    }

    /// <summary>worker 抛错：传播首个异常本体，且不返回部分成功。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenWorkerFails_PropagatesFailure()
    {
        var failure = new InvalidOperationException("consume worker failure");
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 2,
          fragmentSinkCapacity: 2));

        var execution = executor.ExecuteAndConsumeAsync(
          CreateBatches(8),
          (batch, _, _) =>
          {
              if (batch.StableOrder == 2)
              {
                  throw failure;
              }

              return batch.StableOrder;
          },
          _ => { });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        Assert.Same(failure, exception);
    }

    /// <summary>reducer/消费回调抛错：与旧入口一致，异常优先于部分成功返回。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenConsumerFails_PropagatesConsumerFailure()
    {
        var executor = CreateExecutor(2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAndConsumeAsync(
          CreateBatches(6),
          (batch, _, _) => batch.StableOrder,
          _ => throw new InvalidOperationException("consumer failed")));

        Assert.Equal("consumer failed", exception.Message);
    }

    /// <summary>
    /// 生命周期判据（消费入口）：最后一批仍阻塞在 worker 内的中间态下，
    /// 最早已消费的结果必须已不可达。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhileLastBatchIsBlocked_EarlierConsumedResultsAreCollectable()
    {
        var probe = await RunBlockedTailProbeAsync(collectResults: false);

        Assert.Equal(BlockedTailBatchCount - 1, probe.ConsumedAtMidStage);
        Assert.Equal(
          0,
          probe.AlivePrefixAtMidStage);
    }

    /// <summary>
    /// 负对照（旧收集入口）：同样的中间态下，执行器仍必须持有全部已提交结果；
    /// 否则上面的生命周期测试没有区分力。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhileLastBatchIsBlocked_StillRetainsConsumedResults()
    {
        var probe = await RunBlockedTailProbeAsync(collectResults: true);

        Assert.Equal(BlockedTailBatchCount - 1, probe.ConsumedAtMidStage);
        Assert.Equal(
          CollectablePrefixLength,
          probe.AlivePrefixAtMidStage);
    }

    /// <summary>
    /// 消费入口返回非泛型 <see cref="Task"/>，调用点无法再隐式建立结果列表根。
    /// </summary>
    [Fact]
    public void ExecuteAndConsumeAsync_ExposesConsumingSignature()
    {
        var method = typeof(CpgWorkBatchExecutor)
          .GetMethods()
          .Single(candidate => candidate.Name == nameof(CpgWorkBatchExecutor.ExecuteAndConsumeAsync));

        Assert.True(method.IsGenericMethodDefinition);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(6, parameters.Length);
        Assert.Equal("consumeResult", parameters[2].Name);
        Assert.StartsWith("System.Action`1", parameters[2].ParameterType.ToString(), StringComparison.Ordinal);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
        Assert.Equal("stageId", parameters[4].Name);
        Assert.Equal("resultMetrics", parameters[5].Name);
    }

    /// <summary>
    /// 结构判据：消费入口不得为“已提交结果列表”分配容器。
    /// 同步路径完全在调用线程上执行，故可用当前线程分配字节数做确定性测量。
    /// 所有 batch 返回同一个预先构造的载荷实例，使两条路径的逐 batch 载荷分配恒等，
    /// 于是差值只剩收集入口独有的结果列表后备数组。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_DoesNotAllocateResultCollectionContainers()
    {
        const int batchCount = 5000;
        var batches = CreateBatches(batchCount);
        var payload = new ConsumablePayload(order: 0);
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 1,
          useSynchronousExecution: true));
        var sink = 0;

        // 预热两条路径，确保测量区域之外的一次性初始化已完成。
        await executor.ExecuteAsync(batches, (_, _, _) => payload, _ => { });
        await executor.ExecuteAndConsumeAsync(batches, (_, _, _) => payload, _ => { });

        var collectingBytes = GC.GetAllocatedBytesForCurrentThread();
        var collected = await executor.ExecuteAsync(
          batches,
          (_, _, _) => payload,
          _ => { });
        collectingBytes = GC.GetAllocatedBytesForCurrentThread() - collectingBytes;

        var consumingBytes = GC.GetAllocatedBytesForCurrentThread();
        await executor.ExecuteAndConsumeAsync(
          batches,
          (_, _, _) => payload,
          _ =>
          {
              // 只读不存：消费回调不得把结果转存成外部容器。
              sink++;
          });
        consumingBytes = GC.GetAllocatedBytesForCurrentThread() - consumingBytes;

        // 旧入口的返回契约必须完整，否则负对照没有意义。
        Assert.Equal(batchCount, collected.Count);
        Assert.All(collected, item => Assert.Same(payload, item));
        GC.KeepAlive(collected);
        Assert.Equal(batchCount, sink);

        // x64 上 List<T> 每项一个 8 字节引用；收集入口至少要装下这么多引用。
        const int referenceSize = 8;
        var expectedCollectionBytes = batchCount * referenceSize;
        Assert.True(
          collectingBytes - consumingBytes >= expectedCollectionBytes,
          $"The two entries differ by only {collectingBytes - consumingBytes} bytes; expected at least " +
          $"{expectedCollectionBytes} for the result list that only the collecting entry builds.");
        Assert.True(
          consumingBytes < collectingBytes,
          $"The consuming entry allocated {consumingBytes} bytes, not less than the collecting entry's " +
          $"{collectingBytes} bytes.");
    }

    private const int BlockedTailBatchCount = 10;

    // 执行器最多只能保留“worker 当前值 + reducer 最近一个局部值”等少数结果，
    // 因此前 6 个已消费结果必须可回收，留出足够余量避免抖动。
    private const int CollectablePrefixLength = 6;

    /// <summary>
    /// 在阶段仍在运行时采集弱引用并与负对照比较：最后一批阻塞在 processBatch 内，
    /// 前 9 个结果已按 stable order 消费完毕。载荷只出现在执行器内部，
    /// 探针本身不保留任何强引用，也不把载荷捕获进断言委托。
    /// </summary>
    private static async Task<BlockedTailProbe> RunBlockedTailProbeAsync(bool collectResults)
    {
        var references = new WeakReference[BlockedTailBatchCount];
        var consumedCount = 0;
        using var lastBatchEntered = new ManualResetEventSlim();
        using var releaseLastBatch = new ManualResetEventSlim();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2,
          queueCapacity: BlockedTailBatchCount + 1,
          fragmentSinkCapacity: BlockedTailBatchCount + 1));

        void Consume(ConsumablePayload payload)
        {
            references[payload.Order] = new WeakReference(payload);
            Interlocked.Increment(ref consumedCount);
        }

        ConsumablePayload Process(CpgWorkBatch batch, CancellationToken token)
        {
            if (batch.StableOrder == BlockedTailBatchCount - 1)
            {
                lastBatchEntered.Set();
                // 阶段中间态：最后一批仍在 worker 内，尚未产生结果。
                releaseLastBatch.Wait(token);
            }

            return new ConsumablePayload(batch.StableOrder);
        }

        var batches = CreateBatches(BlockedTailBatchCount);
        Task execution = collectResults
          ? executor.ExecuteAsync(
              batches,
              (batch, _, token) => Process(batch, token),
              Consume)
          : executor.ExecuteAndConsumeAsync(
              batches,
              (batch, _, token) => Process(batch, token),
              Consume);

        int consumedAtMidStage;
        int alivePrefixAtMidStage;
        try
        {
            Assert.True(
              lastBatchEntered.Wait(TimeSpan.FromSeconds(20)),
              "The final batch never entered a worker; the mid-stage lifecycle probe is invalid.");
            WaitForConsumedCount(ref consumedCount, BlockedTailBatchCount - 1, TimeSpan.FromSeconds(20));
            consumedAtMidStage = Volatile.Read(ref consumedCount);
            alivePrefixAtMidStage = CountAlivePrefix(references, CollectablePrefixLength);
        }
        finally
        {
            releaseLastBatch.Set();
        }

        await execution.WaitAsync(TimeSpan.FromSeconds(20));
        if (collectResults && execution is Task<IReadOnlyList<ConsumablePayload>> collecting)
        {
            // 旧入口的返回契约必须完整：负对照的强引用确实来自该列表。
            Assert.Equal(BlockedTailBatchCount, (await collecting).Count);
        }

        return new BlockedTailProbe(consumedAtMidStage, alivePrefixAtMidStage);
    }

    /// <summary>
    /// 统计前 <paramref name="count"/> 个已消费载荷中仍可达的数量。
    /// GC 调用只允许出现在这里；本方法不接触载荷本身，避免制造假保留。
    /// </summary>
    private static int CountAlivePrefix(WeakReference[] references, int count)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var alive = 0;
        for (var index = 0; index < count; index++)
        {
            if (references[index] is { IsAlive: true })
            {
                alive++;
            }
        }

        return alive;
    }

    private static void WaitForConsumedCount(ref int consumedCount, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref consumedCount) < expected)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                  $"Only {Volatile.Read(ref consumedCount)} of {expected} results were consumed in time.");
            }

            Thread.Sleep(5);
        }
    }

    private static void WaitForCompletion(int[] completed, int order, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref completed[order]) == 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Batch {order} did not finish processing in time.");
            }

            Thread.Sleep(1);
        }
    }

    private static async Task IgnoreOutcomeAsync(Task execution)
    {
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // 屏障失败路径只用于放行 worker；真实失败仍由主断言报告。
        }
    }

    private static string DescribeLoadIndependentEvent(CpgWorkBatchPerformanceEvent performanceEvent)
    {
        return string.Join(
          "|",
          performanceEvent.StageId,
          performanceEvent.BatchId,
          performanceEvent.StableOrder,
          performanceEvent.InputCount,
          performanceEvent.EstimatedCost,
          performanceEvent.EstimatedBytes,
          performanceEvent.OutputNodeCount,
          performanceEvent.OutputEdgeCount,
          performanceEvent.FragmentBytes);
    }

    private static CpgWorkBatchExecutor CreateExecutor(int maxDegreeOfParallelism)
    {
        return new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism,
          queueCapacity: 8,
          fragmentSinkCapacity: 8));
    }

    private static IReadOnlyList<CpgWorkBatch> CreateBatches(int count)
    {
        return Enumerable.Range(0, count)
          .Select(index => new CpgWorkBatch(
            index,
            "consumption.cs",
            index,
            new[]
            {
                new CpgWorkItem(index, "consumption.cs", $"M{index}", index, index + 1, 1, CpgWorkItemKind.Method),
            },
            1,
            1,
            64,
            CpgWorkBatchKind.Methods))
          .ToArray();
    }

    private sealed record BlockedTailProbe(int ConsumedAtMidStage, int AlivePrefixAtMidStage);

    /// <summary>带独立大数组（LOH）的结果载荷；弱引用可判定它是否仍被累计结果列表保留。</summary>
    private sealed class ConsumablePayload
    {
        private readonly byte[] _buffer;

        public ConsumablePayload(int order)
        {
            Order = order;
            _buffer = new byte[256 * 1024];
            _buffer[0] = (byte)order;
        }

        public int Order { get; }

        public int BufferLength => _buffer.Length;
    }
}
