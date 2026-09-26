using System.Diagnostics;
using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// <summary>
/// 逐 worker 使用率记账的契约。它是一份**诊断**能力，因此断言分两层：
/// ① 记账真的随工作发生（而不是恒定 0 —— 那会让报告永远显示 0% 使用率）；
/// ② 记账不改变调度语义（结果与顺序仍与不采样时一致）。
/// </summary>
public sealed class WorkSchedulerWorkerUtilizationTests
{
    private static WorkScheduler CreateScheduler(int workerCount)
    {
        return new WorkScheduler(new WorkSchedulerOptions(
          DirectoryLimit: workerCount,
          CpgLimit: workerCount,
          RuleGroupLimit: workerCount,
          HelperLimit: workerCount,
          ReplayLimit: workerCount,
          MaxConcurrentOperations: workerCount));
    }

    private static WorkItem<int> Item(long stableOrder, Func<Task<int>> execute)
    {
        return new WorkItem<int>
        {
            StableOrder = stableOrder,
            ExecuteAsync = (_, _) => execute(),
        };
    }

    [Fact]
    public async Task GetWorkerUtilization_BeforeAnySubmission_ReportsNeverStartedWorkers()
    {
        await using var scheduler = CreateScheduler(workerCount: 8);

        var snapshot = scheduler.GetWorkerUtilization();

        // worker 在首次提交时才惰性启动，故此时必须是 null 而不是「0% 空闲」。
        Assert.Equal(8, snapshot.Count);
        Assert.All(snapshot, item => Assert.Null(item));
    }

    [Fact]
    public async Task GetWorkerUtilization_AfterWork_CountsBusyTimeAndItems()
    {
        await using var scheduler = CreateScheduler(workerCount: 4);
        var itemCount = 12;

        await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = Enumerable.Range(0, itemCount)
              .Select(index => Item(index, async () =>
              {
                  await Task.Delay(20);
                  return index;
              }))
              .ToArray(),
            Category = WorkCategories.Default,
        });

        var snapshot = scheduler.GetWorkerUtilization();

        // ① 记账活着：全部工作项必须被某个 worker 认领，且忙碌时间非零。
        Assert.Equal(itemCount, snapshot.Sum(worker => worker?.ExecutedItemCount ?? 0));
        Assert.All(snapshot, worker => Assert.NotNull(worker));
        Assert.True(
          snapshot.Sum(worker => worker!.BusyTime.TotalMilliseconds) > 0,
          "忙碌时间必须被记到，否则使用率报告会恒为 0。");

        // ② 忙碌与空闲之和等于存活时间：漏记任一侧都会让使用率失真。
        foreach (var worker in snapshot)
        {
            var total = worker!.BusyTime + worker.IdleTime;
            Assert.True(
              Math.Abs((total - worker.Lifetime).TotalMilliseconds) < 1.0,
              $"worker {worker.WorkerIndex}: busy+idle={total.TotalMilliseconds}ms 应等于 lifetime={worker.Lifetime.TotalMilliseconds}ms");
        }
    }

    [Fact]
    public async Task GetWorkerUtilization_WhenSerialWork_ConfinesItemsToOneWorker()
    {
        // 限死在 1 个 worker：用来证明「多 worker 汇总」不是把同一份工作量重复计数。
        await using var scheduler = CreateScheduler(workerCount: 1);

        await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = Enumerable.Range(0, 5)
              .Select(index => Item(index, () => Task.FromResult(index)))
              .ToArray(),
            Category = WorkCategories.Default,
        });

        var snapshot = scheduler.GetWorkerUtilization();

        Assert.Single(snapshot);
        Assert.Equal(5, snapshot[0]!.ExecutedItemCount);
    }

    [Fact]
    public async Task GetWorkerUtilization_WhenItemThrows_StillCountsTheItem()
    {
        await using var scheduler = CreateScheduler(workerCount: 1);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
          scheduler.RunAsync(new WorkSubmission<int>
          {
              Items = new[]
              {
                  Item(0, () => throw new InvalidOperationException("boom")),
              },
              Category = WorkCategories.Default,
          }));

        var snapshot = scheduler.GetWorkerUtilization();

        // 被领走并失败的工作项仍占用过 worker，漏计会让使用率偏低。
        Assert.Equal(1, snapshot[0]!.ExecutedItemCount);
    }

    [Fact]
    public void UtilizationRatio_WhenLifetimeIsZero_IsZeroInsteadOfNaN()
    {
        var worker = new WorkerUtilization(
          WorkerIndex: 0,
          ExecutedItemCount: 0,
          BusyTime: TimeSpan.Zero,
          IdleTime: TimeSpan.Zero,
          Lifetime: TimeSpan.Zero);

        Assert.Equal(0, worker.UtilizationRatio);
    }

    [Fact]
    public void UtilizationRatio_WhenBusyExceedsLifetime_ClampsToOne()
    {
        // 两个字段分别读取，理论上可能被抢占推进到 busy > lifetime。
        var worker = new WorkerUtilization(
          WorkerIndex: 0,
          ExecutedItemCount: 1,
          BusyTime: TimeSpan.FromSeconds(5),
          IdleTime: TimeSpan.Zero,
          Lifetime: TimeSpan.FromSeconds(1));

        Assert.Equal(1, worker.UtilizationRatio);
    }
}
