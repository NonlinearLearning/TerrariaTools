using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkSchedulerOrderingTests
{
    [Fact]
    public async Task RunAsync_WhenItemsCompleteOutOfOrder_ReturnsStableOrder()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var items = Enumerable.Range(0, 8).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            EstimatedCost = 8 - index,
            ExecuteAsync = async (_, _) =>
            {
                await Task.Delay((8 - index) * 5);
                return index;
            },
        }).ToArray();

        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = items });

        Assert.Equal(Enumerable.Range(0, 8), results);
    }

    [Fact]
    public async Task RunAsync_WhenStableOrderIsNotContiguous_StillSortsByStableOrder()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var items = new[]
        {
            new WorkItem<string> { StableOrder = 40, ExecuteAsync = (_, _) => Task.FromResult("c") },
            new WorkItem<string> { StableOrder = 10, ExecuteAsync = (_, _) => Task.FromResult("a") },
            new WorkItem<string> { StableOrder = 25, ExecuteAsync = (_, _) => Task.FromResult("b") },
        };

        var results = await scheduler.RunAsync(new WorkSubmission<string> { Items = items });

        Assert.Equal(new[] { "a", "b", "c" }, results);
    }

    [Fact]
    public async Task RunAsync_WhenStableOrderIsDuplicated_ThrowsBeforeExecuting()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var executed = 0;
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 1,
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(1); },
            },
            new WorkItem<int>
            {
                StableOrder = 1,
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(2); },
            },
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
          scheduler.RunAsync(new WorkSubmission<int> { Items = items }));

        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task RunAsync_WhenSubmissionIsEmpty_ReturnsEmptyWithoutStartingWork()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = Array.Empty<WorkItem<int>>() });

        Assert.Empty(results);
    }

    [Fact]
    public async Task RunAsync_WhenPreserveOrderIsFalse_ReturnsEmptyList()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var executed = 0;
        var items = Enumerable.Range(0, 6).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(index); },
        }).ToArray();

        var results = await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = items,
            PreserveOrder = false,
        });

        Assert.Empty(results);
        Assert.Equal(6, Volatile.Read(ref executed));
    }

    [Fact]
    public async Task RunAsync_WhenMaxConcurrencyIsOne_NeverRunsTwoItemsAtOnce()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(8, 8, 8, 8, 8, 8));
        var active = 0;
        var peak = 0;
        var gate = new object();
        var items = Enumerable.Range(0, 12).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            ExecuteAsync = async (_, _) =>
            {
                lock (gate)
                {
                    active++;
                    peak = Math.Max(peak, active);
                }

                await Task.Delay(5);

                lock (gate)
                {
                    active--;
                }

                return index;
            },
        }).ToArray();

        await scheduler.RunAsync(new WorkSubmission<int> { Items = items, MaxConcurrency = 1 });

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task RunAsync_WhenHigherCostIsReady_StartsItBeforeLowerCost()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(1, 1, 1, 1, 1, 1));
        var startOrder = new List<long>();
        var gate = new object();
        var items = new[]
        {
            new WorkItem<long>
            {
                StableOrder = 0,
                EstimatedCost = 1,
                ExecuteAsync = (_, _) => { lock (gate) { startOrder.Add(0); } return Task.FromResult(0L); },
            },
            new WorkItem<long>
            {
                StableOrder = 1,
                EstimatedCost = 900,
                ExecuteAsync = (_, _) => { lock (gate) { startOrder.Add(1); } return Task.FromResult(1L); },
            },
        };

        await scheduler.RunAsync(new WorkSubmission<long> { Items = items });

        // LPT：成本降序，最长的先启动。
        Assert.Equal(new long[] { 1, 0 }, startOrder);
    }

    [Fact]
    public async Task RunAsync_WhenCostsAreEqual_StartsInAscendingStableOrder()
    {
        // 成本相等时以 StableOrder 升序破平。内核的就绪序由按
        // (-cost, StableOrder) 排序的优先队列给出，该键必须在单次提交内**全序**
        // （StableOrder 唯一性由 Create 的重复校验保证）；若把 StableOrder 从键里
        // 去掉，堆序退化为不确定，本测试会失败。
        // 老化提升用大阈值关闭，使顺序只由排序键决定、不依赖挂钟。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(1, 1, 1, 1, 1, 1)
        {
            MaximumThroughputWait = TimeSpan.FromHours(1),
        });
        var startOrder = new List<long>();
        var gate = new object();

        WorkItem<long> CreateItem(long order)
        {
            return new WorkItem<long>
            {
                StableOrder = order,
                EstimatedCost = 50,
                ExecuteAsync = (_, _) => { lock (gate) { startOrder.Add(order); } return Task.FromResult(order); },
            };
        }

        // 刻意逆序声明，使 StableOrder 与数组下标相反：若实现退回“按下标取”，
        // 得到的顺序会与此断言不同。
        var items = new[] { CreateItem(3), CreateItem(1), CreateItem(2) };

        await scheduler.RunAsync(new WorkSubmission<long> { Items = items, MaxConcurrency = 1 });

        Assert.Equal(new long[] { 1, 2, 3 }, startOrder);
    }

    [Fact]
    public async Task WorkerCount_WhenLimitsDiffer_MatchesMaximumLimit()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 12, 3, 4, 5, 6));

        Assert.Equal(12, scheduler.WorkerCount);
    }
}
