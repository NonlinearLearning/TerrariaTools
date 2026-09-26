using System.Diagnostics;
using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

/// 固定内核的就绪序老化提升契约（设计 §3.2，风险项「老化提升无专门测试」）。
/// 吞吐项（<see cref="WorkPriority.Throughput"/>）等待超过
/// <see cref="WorkSchedulerOptions.MaximumThroughputWait"/> 后，
/// 在与延迟敏感项竞争时按优先级 0 处理，避免被持续挤占。
public sealed class WorkSchedulerAgingTests
{
    // 用 0 阈值使「已老化」在判定时恒成立，从而无需依赖真实等待时间即可确定性触发。
    private static WorkScheduler CreateScheduler(TimeSpan maximumThroughputWait)
    {
        return new WorkScheduler(new WorkSchedulerOptions(1, 1, 1, 1, 1, 1)
        {
            MaximumThroughputWait = maximumThroughputWait,
        });
    }

    private static (WorkItem<long> Throughput, WorkItem<long> Latency) CreateCompetingItems(
      List<long> startOrder,
      object gate)
    {
        var throughput = new WorkItem<long>
        {
            StableOrder = 0,
            Priority = WorkPriority.Throughput,
            EstimatedCost = 900,
            ExecuteAsync = (_, _) =>
            {
                lock (gate)
                {
                    startOrder.Add(0);
                }

                return Task.FromResult(0L);
            },
        };
        var latency = new WorkItem<long>
        {
            StableOrder = 1,
            Priority = WorkPriority.LatencySensitive,
            EstimatedCost = 1,
            ExecuteAsync = (_, _) =>
            {
                lock (gate)
                {
                    startOrder.Add(1);
                }

                return Task.FromResult(1L);
            },
        };
        return (throughput, latency);
    }

    [Fact]
    public async Task RunAsync_WhenThroughputItemAgesOutranksLowerCostLatencyItem()
    {
        await using var scheduler = CreateScheduler(TimeSpan.Zero);
        var startOrder = new List<long>();
        var gate = new object();
        var (throughput, latency) = CreateCompetingItems(startOrder, gate);

        await scheduler.RunAsync(new WorkSubmission<long>
        {
            Items = new[] { throughput, latency },
            MaxConcurrency = 1,
        });

        // 两者都被提升到优先级 0 后，按成本降序 ⇒ 吞吐项（900）先于延迟项（1）。
        Assert.Equal(new long[] { 0, 1 }, startOrder);
    }

    [Fact]
    public async Task RunAsync_WhenThroughputItemHasNotAged_LatencyItemStartsFirst()
    {
        await using var scheduler = CreateScheduler(TimeSpan.FromSeconds(2));
        var startOrder = new List<long>();
        var gate = new object();
        var (throughput, latency) = CreateCompetingItems(startOrder, gate);

        await scheduler.RunAsync(new WorkSubmission<long>
        {
            Items = new[] { throughput, latency },
            MaxConcurrency = 1,
        });

        // 未老化：延迟敏感项优先级 0 < 吞吐项 1，故成本更高也不抢先。
        Assert.Equal(new long[] { 1, 0 }, startOrder);
    }

    [Fact]
    public async Task RunAsync_WhenAgingApplies_LatencyItemStillOutranksThroughputOnCost()
    {
        await using var scheduler = CreateScheduler(TimeSpan.Zero);
        var startOrder = new List<long>();
        var gate = new object();
        var latency = new WorkItem<long>
        {
            StableOrder = 0,
            Priority = WorkPriority.LatencySensitive,
            EstimatedCost = 900,
            ExecuteAsync = (_, _) =>
            {
                lock (gate)
                {
                    startOrder.Add(0);
                }

                return Task.FromResult(0L);
            },
        };
        var throughput = new WorkItem<long>
        {
            StableOrder = 1,
            Priority = WorkPriority.Throughput,
            EstimatedCost = 1,
            ExecuteAsync = (_, _) =>
            {
                lock (gate)
                {
                    startOrder.Add(1);
                }

                return Task.FromResult(1L);
            },
        };

        await scheduler.RunAsync(new WorkSubmission<long>
        {
            Items = new[] { latency, throughput },
            MaxConcurrency = 1,
        });

        // 老化只做「提升」，不做「降级」：延迟项本来就在优先级 0，成本仍决定次序。
        Assert.Equal(new long[] { 0, 1 }, startOrder);
    }
}
