using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchLifecycleTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCanceledBeforeEnqueue_DoesNotProcessBatches()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var processed = 0;
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync<LocalCpgFragment>(
          CreateBatches(8),
          (batch, _, _) =>
          {
              Interlocked.Increment(ref processed);
              return EmptyFragment(batch);
          },
          cancellationToken: cancellation.Token));

        Assert.Equal(0, processed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCanceledDuringAnalysis_StopsAcceptingNewBatches()
    {
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        var processed = 0;
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2,
          queueCapacity: 1,
          fragmentSinkCapacity: 1));
        var execution = executor.ExecuteAsync<LocalCpgFragment>(
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
              return EmptyFragment(batch);
          },
          cancellationToken: cancellation.Token);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(Volatile.Read(ref processed) < 12);
    }

    [Fact]
    public async Task ExecuteAsync_WhenReducerFails_DoesNotReturnPartialSuccess()
    {
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<LocalCpgFragment>(
          CreateBatches(6),
          (batch, _, _) => EmptyFragment(batch),
          _ => throw new InvalidOperationException("reducer failed")));

        Assert.Equal("reducer failed", exception.Message);
    }

    /// <summary>
    /// 旧收集入口的返回契约：数量、顺序和内容必须完整；
    /// 这是消费入口的负对照基线，不得因为新增入口而被稀释。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReturnsCompleteListInStableOrder()
    {
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(3));

        // 输入顺序打乱，返回列表仍必须按 stable order 排定。
        var fragments = await executor.ExecuteFragmentsAsync(
          CreateBatches(10).Reverse().ToArray(),
          (batch, _, _) => EmptyFragment(batch));

        Assert.Equal(10, fragments.Count);
        Assert.Equal(Enumerable.Range(0, 10), fragments.Select(fragment => fragment.StableOrder));
        Assert.All(fragments, fragment => Assert.Equal("lifecycle.cs", fragment.SourceFilePath));
    }

    /// <summary>两个入口对同一输入必须给出相同的消费顺序；旧入口额外返回完整列表。</summary>
    [Fact]
    public async Task ExecuteAndConsumeAsync_ConsumesInSameOrderAsCollectedEntry()
    {
        var batches = CreateBatches(7);
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(2));
        var consumedOrders = new List<int>();

        var collected = await executor.ExecuteAsync(
          batches,
          (batch, _, _) => batch.StableOrder);

        await executor.ExecuteAndConsumeAsync(
          batches,
          (batch, _, _) => batch.StableOrder,
          consumedOrders.Add);

        Assert.Equal(collected, consumedOrders);
    }

    private static IReadOnlyList<CpgWorkBatch> CreateBatches(int count)
    {
        return Enumerable.Range(0, count)
          .Select(index => new CpgWorkBatch(
            index,
            "lifecycle.cs",
            index,
            new[]
            {
                new CpgWorkItem(index, "lifecycle.cs", $"M{index}", index, index + 1, 1, CpgWorkItemKind.Method),
            },
            1,
            1,
            64,
            CpgWorkBatchKind.Methods))
          .ToArray();
    }

    private static LocalCpgFragment EmptyFragment(CpgWorkBatch batch)
    {
        return new LocalCpgFragment(
          batch.BatchId,
          batch.SourceFilePath,
          batch.StableOrder,
          Array.Empty<CpgNodeDescriptor>(),
          Array.Empty<CpgEdgeCandidate>(),
          Array.Empty<CpgMethodSummary>(),
          Array.Empty<CpgBoundaryReference>(),
          new CpgFragmentMetrics(0, 0, 0, 0, 0, 0, 0),
          Array.Empty<CpgDiagnostic>());
    }
}
