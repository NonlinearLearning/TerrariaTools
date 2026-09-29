using System.Diagnostics;
using NL.Concurrency;
using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// <c>CpgWorkBatchExecutor</c> 的 per-worker 使用率记账契约。
/// </summary>
/// <remarks>
/// 记账是诊断能力，断言分三层：① 记账随真实工作发生（恒 0 会让报告永远显示 0% 使用率）；
/// ② 不改变执行语义（结果与顺序仍与不记账时一致）；
/// ③ 跨多次执行（多个池）按同一 workerIndex 正确累加——这是 NLISSN 每个文件新建
/// builder 的真实形态，只测单池会漏掉这条唯一的聚合路径。
/// </remarks>
public sealed class CpgWorkBatchExecutorWorkerUtilizationTests
{
    private static CpgWorkBatch Batch(int stableOrder)
    {
        return new CpgWorkBatch(
          batchId: stableOrder,
          sourceFilePath: "Input.cs",
          stableOrder: stableOrder,
          items: Array.Empty<CpgWorkItem>(),
          estimatedCost: 1,
          estimatedNodeCount: 0,
          estimatedBytes: 1,
          kind: CpgWorkBatchKind.Methods);
    }

    private static CpgWorkBatchExecutor CreateExecutor(
      int workerCount,
      CpgWorkerUtilizationCollector collector)
    {
        return new CpgWorkBatchExecutor(
          new CpgWorkBatchExecutorOptions(
            workerCount,
            workerUtilizationCollector: collector),
          new BoundedConcurrencyPool());
    }

    [Fact]
    public async Task ExecuteAsync_WithCollector_ReportsBusyTimeForEveryWorker()
    {
        var collector = new CpgWorkerUtilizationCollector();
        var executor = CreateExecutor(workerCount: 8, collector);

        await executor.ExecuteAsync(
          Enumerable.Range(0, 16).Select(index => Batch(index)).ToArray(),
          (batch, _, _) =>
          {
              // 有意的忙等：使用率是时间比值，纯计算任务若立刻返回，
              // 忙碌时间会小到无法与"没记账"区分。
              Thread.Sleep(5);
              return batch.StableOrder;
          });

        var snapshot = collector.Snapshot();

        Assert.Equal(8, snapshot.Count);
        Assert.All(snapshot, worker => Assert.True(worker.ExecutedBatchCount > 0));
        Assert.All(snapshot, worker => Assert.True(worker.BusyTime > TimeSpan.Zero));
        Assert.All(snapshot, worker => Assert.Equal(16, snapshot.Sum(item => item.ExecutedBatchCount)));
        // 忙碌 + 空闲必须恰好等于存活期，否则使用率的分母是错的。
        Assert.All(
          snapshot,
          worker => Assert.True(
            Math.Abs((worker.BusyTime + worker.IdleTime - worker.Lifetime).TotalMilliseconds) < 1.0,
            $"worker {worker.WorkerIndex}: busy={worker.BusyTime.TotalMilliseconds} idle={worker.IdleTime.TotalMilliseconds} lifetime={worker.Lifetime.TotalMilliseconds}"));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutCollector_KeepsSchedulingSemanticsAndWritesNothing()
    {
        var withCollector = new CpgWorkerUtilizationCollector();
        var explicitExecutor = CreateExecutor(workerCount: 4, withCollector);
        var plainExecutor = new CpgWorkBatchExecutor(
          new CpgWorkBatchExecutorOptions(4),
          new BoundedConcurrencyPool());

        var batches = Enumerable.Range(0, 12).Select(index => Batch(index)).ToArray();
        Func<CpgWorkBatch, int, CancellationToken, int> process = (batch, _, _) => (int)batch.StableOrder;

        var withAccounting = await explicitExecutor.ExecuteAsync(batches, process);
        var withoutAccounting = await plainExecutor.ExecuteAsync(batches, process);

        // 记账不得改变归并顺序或结果。
        Assert.Equal(withoutAccounting, withAccounting);
        // 未注入时连 worker 数都不该被观测到。
        Assert.Equal(0, new CpgWorkerUtilizationCollector().ObservedWorkerCount);
    }

    [Fact]
    public async Task ExecuteAsync_AcrossMultipleExecutions_AccumulatesByWorkerIndex()
    {
        var collector = new CpgWorkerUtilizationCollector();
        var executor = CreateExecutor(workerCount: 8, collector);

        // 两个独立执行序列，模拟 NLISSN"每个文件一个新 builder"的形态。
        for (var round = 0; round < 2; round++)
        {
            await executor.ExecuteAsync(
              Enumerable.Range(0, 8).Select(index => Batch(index)).ToArray(),
              (batch, _, _) => batch.StableOrder);
        }

        // 一个 executor 的每次 ExecuteAsync 都会新建 own worker 池并各自登记一次。
        Assert.Equal(2, collector.PoolExecutionCount);
        Assert.Equal(8, collector.ObservedWorkerCount);
        // 8 个 batch × 2 轮，每个下标各执行 1 次 → 恰好 16。
        Assert.Equal(16, collector.Snapshot().Sum(worker => worker.ExecutedBatchCount));
    }

    [Fact]
    public async Task Snapshot_ConcurrentWrites_DoNotLoseCounts()
    {
        var collector = new CpgWorkerUtilizationCollector();
        // 并发写入是真实形态：8 个文件并发构建 = 8 个池同时记账。
        Parallel.For(0, 8, _ =>
        {
            var executor = CreateExecutor(workerCount: 2, collector);
            executor.ExecuteAsync(
              Enumerable.Range(0, 4).Select(index => Batch(index)).ToArray(),
              (batch, _, _) => batch.StableOrder).GetAwaiter().GetResult();
        });

        Assert.Equal(8, collector.PoolExecutionCount);
        // 8 个池 × 4 个 batch，一个都不能丢。
        Assert.Equal(32, collector.Snapshot().Sum(worker => worker.ExecutedBatchCount));
    }

    [Fact]
    public async Task ExecuteAsync_FailingBatch_StillCountsItsBusyTime()
    {
        var collector = new CpgWorkerUtilizationCollector();
        var executor = CreateExecutor(workerCount: 2, collector);

        await Assert.ThrowsAnyAsync<Exception>(() => executor.ExecuteAsync<int>(
          new[] { Batch(0), Batch(1) },
          (batch, _, _) =>
          {
              Thread.Sleep(5);
              throw new InvalidOperationException("intentional");
          }));

        // 失败的 batch 同样占用了 worker；漏记会把失败运行报成"worker 很闲"。
        Assert.True(collector.Snapshot().Sum(worker => worker.BusyTime.TotalMilliseconds) > 0);
    }

    [Fact]
    public void UtilizationRatio_ZeroLifetime_IsZeroNotNaN()
    {
        var worker = new CpgWorkerUtilization(0, 0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(0, worker.UtilizationRatio);
    }

    [Fact]
    public void UtilizationRatio_ClampsToUnitInterval()
    {
        // 存活期与忙碌时间分别读取，二者之间的抢占可能让比值略微超过 1。
        var worker = new CpgWorkerUtilization(
          0,
          1,
          TimeSpan.FromSeconds(2),
          TimeSpan.Zero,
          TimeSpan.FromSeconds(1));

        Assert.Equal(1, worker.UtilizationRatio);
    }

    [Fact]
    public async Task ExecuteAsync_SingleBatch_IsConfinedToAccountingOfOneWorker()
    {
        var collector = new CpgWorkerUtilizationCollector();
        var executor = CreateExecutor(workerCount: 4, collector);

        await executor.ExecuteAsync(new[] { Batch(0) }, (batch, _, _) => batch.StableOrder);

        var snapshot = collector.Snapshot();
        Assert.Equal(4, snapshot.Count);
        Assert.Equal(1, snapshot.Sum(worker => worker.ExecutedBatchCount));
    }
}
