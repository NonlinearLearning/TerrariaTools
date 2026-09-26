using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.PerformanceTests.Concurrency;

public sealed class CpgWorkBatchExecutorContractTests
{
    [Fact]
    public async Task ExecuteAsync_StartsExactlyConfiguredLongLivedWorkers_AndEachWorkerProcessesSeveralBatches()
    {
        var batches = CreateBatches(12);
        var workerBatchCounts = new int[3];
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 8,
          fragmentSinkCapacity: 8));

        var fragments = await executor.ExecuteFragmentsAsync(
          batches,
          (batch, workerIndex, _) =>
          {
              Interlocked.Increment(ref workerBatchCounts[workerIndex]);
              return EmptyFragment(batch);
          });

        Assert.Equal(batches.Count, fragments.Count);
        Assert.All(workerBatchCounts, count => Assert.True(count >= 2));
        Assert.Equal(3, executor.WorkerCount);
    }

    [Fact]
    public async Task ExecuteAsync_UsesBoundedFragmentSink_WhenReducerIsSlowerThanWorkers()
    {
        var batches = CreateBatches(12);
        var processed = 0;
        var reducerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseReducer = new ManualResetEventSlim();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 1,
          fragmentSinkCapacity: 1));

        var execution = executor.ExecuteFragmentsAsync(
          batches,
          (batch, _, _) =>
          {
              Interlocked.Increment(ref processed);
              return EmptyFragment(batch);
          },
          fragment =>
          {
              reducerStarted.TrySetResult(true);
              releaseReducer.Wait();
          });

        await reducerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.True(Volatile.Read(ref processed) < batches.Count);
        releaseReducer.Set();
        await execution;
    }

    [Fact]
    public async Task ExecuteAsync_WithOneWorker_ReducesFragmentsInStableInputOrder()
    {
        var batches = CreateBatches(6).Reverse().ToArray();
        var reducedOrders = new List<int>();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 1,
          queueCapacity: 8,
          fragmentSinkCapacity: 8));

        await executor.ExecuteFragmentsAsync(
          batches,
          (batch, _, _) => EmptyFragment(batch),
          fragment => reducedOrders.Add(fragment.StableOrder));

        Assert.Equal(batches.Select(batch => batch.StableOrder).OrderBy(order => order), reducedOrders);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCanceled_CompletesWithoutAcceptingNewBatches()
    {
        var batches = CreateBatches(20);
        using var cancellation = new CancellationTokenSource();
        using var releaseWorkers = new ManualResetEventSlim();
        var started = 0;
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2,
          queueCapacity: 1,
          fragmentSinkCapacity: 1));

        var execution = executor.ExecuteFragmentsAsync(
          batches,
          (batch, _, token) =>
          {
              Interlocked.Increment(ref started);
              releaseWorkers.Wait(token);
              return EmptyFragment(batch);
          },
          cancellationToken: cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref started) > 0);
        cancellation.Cancel();
        releaseWorkers.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);
        var startedAfterCancellation = Volatile.Read(ref started);
        await Task.Delay(50);
        Assert.Equal(startedAfterCancellation, Volatile.Read(ref started));
    }

    [Fact]
    public async Task ExecuteAsync_WhenWorkerFails_PropagatesTheFirstFailureAndDoesNotReturnPartialSuccess()
    {
        var batches = CreateBatches(8);
        var failure = new InvalidOperationException("worker failure");
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 2,
          fragmentSinkCapacity: 2));

        var execution = executor.ExecuteFragmentsAsync(
          batches,
          (batch, _, _) =>
          {
              if (batch.StableOrder == 2)
              {
                  throw failure;
              }

              return EmptyFragment(batch);
          });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await execution);
        Assert.Same(failure, exception);
    }

    /// <summary>
    /// 消费入口保持与收集入口相同的 worker 并发护栏：
    /// 仍然启动配置数量的长期 worker，且每个 worker 处理多个 batch。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_StartsExactlyConfiguredLongLivedWorkers()
    {
        var batches = CreateBatches(12);
        var workerBatchCounts = new int[3];
        var consumedOrders = new List<int>();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 8,
          fragmentSinkCapacity: 8));

        await executor.ExecuteAndConsumeAsync(
          batches,
          (batch, workerIndex, _) =>
          {
              Interlocked.Increment(ref workerBatchCounts[workerIndex]);
              return batch.StableOrder;
          },
          consumedOrders.Add);

        Assert.Equal(batches.Count, consumedOrders.Count);
        Assert.All(workerBatchCounts, count => Assert.True(count >= 2));
        Assert.Equal(3, executor.WorkerCount);
    }

    /// <summary>
    /// 消费入口仍受有界 fragment sink 约束：reducer/消费回调慢于 worker 时，
    /// 未处理的 batch 数不得达到全量。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_UsesBoundedFragmentSink_WhenConsumerIsSlowerThanWorkers()
    {
        var batches = CreateBatches(12);
        var processed = 0;
        var consumerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseConsumer = new ManualResetEventSlim();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 3,
          queueCapacity: 1,
          fragmentSinkCapacity: 1));

        var execution = executor.ExecuteAndConsumeAsync(
          batches,
          (batch, _, _) =>
          {
              Interlocked.Increment(ref processed);
              return batch.StableOrder;
          },
          _ =>
          {
              consumerStarted.TrySetResult(true);
              releaseConsumer.Wait();
          });

        await consumerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.True(Volatile.Read(ref processed) < batches.Count);
        releaseConsumer.Set();
        await execution;
    }

    /// <summary>
    /// 消费入口的取消传播与收集入口一致：停止接受新 batch，并以取消异常收尾。
    /// </summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_WhenCanceled_CompletesWithoutAcceptingNewBatches()
    {
        var batches = CreateBatches(20);
        using var cancellation = new CancellationTokenSource();
        using var releaseWorkers = new ManualResetEventSlim();
        var started = 0;
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2,
          queueCapacity: 1,
          fragmentSinkCapacity: 1));

        var execution = executor.ExecuteAndConsumeAsync(
          batches,
          (batch, _, token) =>
          {
              Interlocked.Increment(ref started);
              releaseWorkers.Wait(token);
              return batch.StableOrder;
          },
          _ => { },
          cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref started) > 0);
        cancellation.Cancel();
        releaseWorkers.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);
        var startedAfterCancellation = Volatile.Read(ref started);
        await Task.Delay(50);
        Assert.Equal(startedAfterCancellation, Volatile.Read(ref started));
    }

    private static IReadOnlyList<CpgWorkBatch> CreateBatches(int count)
    {
        return Enumerable.Range(0, count)
          .Select(index => new CpgWorkBatch(
            batchId: index,
            sourceFilePath: "sample.cs",
            stableOrder: index,
            items: new[]
            {
                new CpgWorkItem(
                  stableOrder: index,
                  sourceFilePath: "sample.cs",
                  methodSymbolKey: $"M{index}",
                  spanStart: index,
                  spanEnd: index,
                  estimatedCost: 1,
                  kind: CpgWorkItemKind.Method),
            },
            estimatedCost: 1,
            estimatedNodeCount: 1,
            estimatedBytes: 1,
            kind: CpgWorkBatchKind.Methods))
          .ToArray();
    }

    private static LocalCpgFragment EmptyFragment(CpgWorkBatch batch)
    {
        return new LocalCpgFragment(
          batch.BatchId,
          batch.SourceFilePath,
          batch.StableOrder,
          Array.Empty<NLCPG.Builder.Streaming.CpgNodeDescriptor>(),
          Array.Empty<NLCPG.Builder.Streaming.CpgEdgeCandidate>(),
          Array.Empty<CpgMethodSummary>(),
          Array.Empty<CpgBoundaryReference>(),
          CpgFragmentMetrics.Empty,
          Array.Empty<CpgDiagnostic>());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected worker state was not reached.");
            }

            await Task.Delay(10);
        }
    }
}
