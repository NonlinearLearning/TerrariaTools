using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkSchedulerByteBudgetTests
{
    [Fact]
    public async Task RunAsync_WhenMaxInFlightBytesSet_NeverExceedsBudget()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(8, 8, 8, 8, 8, 8));
        var peakBytes = 0L;
        var currentBytes = 0L;
        var gate = new object();
        var items = Enumerable.Range(0, 16).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            EstimatedBytes = 10,
            ExecuteAsync = async (_, _) =>
            {
                lock (gate)
                {
                    currentBytes += 10;
                    peakBytes = Math.Max(peakBytes, currentBytes);
                }

                await Task.Delay(10);

                lock (gate)
                {
                    currentBytes -= 10;
                }

                return index;
            },
        }).ToArray();

        await scheduler.RunAsync(new WorkSubmission<int> { Items = items, MaxInFlightBytes = 30 });

        Assert.True(peakBytes <= 30, $"peak in-flight bytes {peakBytes} exceeded 30");
    }

    [Fact]
    public async Task RunAsync_WhenEstimatedBytesIsZero_NeverBlocksTheItem()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var completed = 0;
        var items = Enumerable.Range(0, 6).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            ExecuteAsync = async (_, _) =>
            {
                await Task.Delay(5);
                Interlocked.Increment(ref completed);
                return index;
            },
        }).ToArray();

        // 额度 1 字节 + 全部估算为 0：必须全部完成而不是永久等待。
        var run = scheduler.RunAsync(new WorkSubmission<int> { Items = items, MaxInFlightBytes = 1 });
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(run, finished);
        Assert.Equal(6, Volatile.Read(ref completed));
    }

    [Fact]
    public async Task RunAsync_WhenSingleItemExceedsBudget_StillRunsIt()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var executed = 0;
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                EstimatedBytes = 10_000,
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(0); },
            },
        };

        var run = scheduler.RunAsync(new WorkSubmission<int> { Items = items, MaxInFlightBytes = 64 });
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(run, finished);
        Assert.Equal(new[] { 0 }, await run);
        Assert.Equal(1, Volatile.Read(ref executed));
    }

    [Fact]
    public async Task RunAsync_WhenBudgetIsZero_DoesNotThrottle()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var active = 0;
        var peak = 0;
        var gate = new object();
        var items = Enumerable.Range(0, 8).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            EstimatedBytes = 100_000,
            ExecuteAsync = async (_, _) =>
            {
                lock (gate)
                {
                    active++;
                    peak = Math.Max(peak, active);
                }

                await Task.Delay(20);

                lock (gate)
                {
                    active--;
                }

                return index;
            },
        }).ToArray();

        await scheduler.RunAsync(new WorkSubmission<int> { Items = items, MaxInFlightBytes = 0 });

        Assert.True(peak > 1, $"expected unthrottled concurrency, peak was {peak}");
    }

    [Fact]
    public async Task RunAsync_WhenInFlightPlusEstimateOverflowsInt64_StillHonoursTheBudget()
    {
        // 已有的 legacy 层用溢出安全式 `_queuedBytes <= _maxBytes - batch.EstimatedBytes`
        // （CpgWorkBatchExecutor.cs:701）。内核若写成 `InFlight + bytes <= Max`，
        // 在 InFlight 接近 long.MaxValue 时会回绕成负数，从而**错误放行**超出预算的项。
        // 这里用「额度恰为 long.MaxValue、第一项就占满额度」构造该场景：
        // 第二项只加 100 字节，正确行为是必须等待，而不是与第一项并发。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(8, 8, 8, 8, 8, 8));
        var gate = new object();
        var active = 0;
        var peak = 0;

        WorkItem<int> CreateItem(long order, long estimatedBytes)
        {
            return new WorkItem<int>
            {
                StableOrder = order,
                EstimatedBytes = estimatedBytes,
                ExecuteAsync = async (_, _) =>
                {
                    lock (gate)
                    {
                        active++;
                        peak = Math.Max(peak, active);
                    }

                    await Task.Delay(150);

                    lock (gate)
                    {
                        active--;
                    }

                    return (int)order;
                },
            };
        }

        var items = new[]
        {
            CreateItem(0, long.MaxValue),
            CreateItem(1, 100),
        };

        var run = scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = items,
            MaxInFlightBytes = long.MaxValue,
        });

        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(run, finished);

        // 额度被第一项占满后，第二项的 100 字节放不进去，必须串行。
        Assert.True(peak == 1, $"expected the budget to serialize the two items, peak concurrency was {peak}");
    }

    [Fact]
    public async Task RunAsync_WhenEstimatedBytesIsNegative_FailsBeforeExecutingAnything()
    {
        // G0-M 要求明确「非法项」策略。负的 EstimatedBytes 会污染在途记账
        // （归还时 `InFlightBytes -= 负数` 反而把计数推高），必须在执行前诊断性拒绝，
        // 与「重复 StableOrder / 缺失依赖」同样是提交前校验。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var executed = 0;
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                EstimatedBytes = -1,
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(0); },
            },
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
          () => scheduler.RunAsync(new WorkSubmission<int> { Items = items, MaxInFlightBytes = 1024 }));

        Assert.Equal(0, Volatile.Read(ref executed));
    }

    [Fact]
    public async Task RunAsync_WhenEstimatedCostIsNegative_FailsBeforeExecutingAnything()
    {
        // 与 EstimatedCost 同样是估算量，负值无意义：它会让按成本就绪排序
        // （WorkScheduler 中以 `cost > bestCost` 选下一个）失去意义。
        // 与负 EstimatedBytes 一致，在提交前诊断性拒绝。
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var executed = 0;
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                EstimatedCost = -1,
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(0); },
            },
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
          () => scheduler.RunAsync(new WorkSubmission<int> { Items = items }));

        Assert.Equal(0, Volatile.Read(ref executed));
    }
}
