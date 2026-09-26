using System.Collections.Concurrent;
using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkSchedulerDependencyTests
{
    [Fact]
    public async Task RunAsync_WhenDependenciesDeclared_ExecutesDependenciesFirst()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var completionOrder = new ConcurrentQueue<long>();
        var items = new[]
        {
            new WorkItem<long> { StableOrder = 0, ExecuteAsync = (_, _) => { completionOrder.Enqueue(0); return Task.FromResult(0L); } },
            new WorkItem<long> { StableOrder = 1, Dependencies = new long[] { 0 }, ExecuteAsync = (_, _) => { completionOrder.Enqueue(1); return Task.FromResult(1L); } },
            new WorkItem<long> { StableOrder = 2, Dependencies = new long[] { 1 }, ExecuteAsync = (_, _) => { completionOrder.Enqueue(2); return Task.FromResult(2L); } },
        };

        var results = await scheduler.RunAsync(new WorkSubmission<long> { Items = items });

        Assert.Equal(new long[] { 0, 1, 2 }, completionOrder.ToArray());
        Assert.Equal(new long[] { 0, 1, 2 }, results);
    }

    [Fact]
    public async Task RunAsync_WhenDependencyIsMissing_ThrowsBeforeExecuting()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(1, 1, 1, 1, 1, 1));
        var items = new[]
        {
            new WorkItem<int> { StableOrder = 0, Dependencies = new long[] { 99 }, ExecuteAsync = (_, _) => Task.FromResult(0) },
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
          scheduler.RunAsync(new WorkSubmission<int> { Items = items }));
    }

    [Fact]
    public async Task RunAsync_WhenDependencyResultsAreDeclared_DependentReceivesThem()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var items = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(20) },
            new WorkItem<int> { StableOrder = 1, ExecuteAsync = (_, _) => Task.FromResult(22) },
            new WorkItem<int>
            {
                StableOrder = 2,
                Dependencies = new long[] { 0, 1 },
                ExecuteAsync = (inputs, _) => Task.FromResult(inputs[0] + inputs[1]),
            },
        };

        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = items });

        Assert.Equal(new[] { 20, 22, 42 }, results);
    }

    [Fact]
    public async Task RunAsync_WhenFanOutAndFanIn_RespectsAllEdges()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var completed = new ConcurrentDictionary<long, bool>();
        var items = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = async (_, _) => { await Task.Delay(5); completed[0] = true; return 1; } },
            new WorkItem<int> { StableOrder = 1, Dependencies = new long[] { 0 }, ExecuteAsync = async (_, _) => { await Task.Delay(10); completed[1] = true; return 2; } },
            new WorkItem<int> { StableOrder = 2, Dependencies = new long[] { 0 }, ExecuteAsync = async (_, _) => { await Task.Delay(5); completed[2] = true; return 4; } },
            new WorkItem<int>
            {
                StableOrder = 3,
                Dependencies = new long[] { 1, 2 },
                ExecuteAsync = (inputs, _) =>
                {
                    Assert.True(completed.ContainsKey(1));
                    Assert.True(completed.ContainsKey(2));
                    return Task.FromResult(inputs[1] + inputs[2]);
                },
            },
        };

        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = items });

        Assert.Equal(new[] { 1, 2, 4, 6 }, results);
    }

    [Fact]
    public async Task RunAsync_WhenDependencyGraphHasCycle_ThrowsInvalidOperation()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var executed = 0;
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                Dependencies = new long[] { 1 },
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(0); },
            },
            new WorkItem<int>
            {
                StableOrder = 1,
                Dependencies = new long[] { 0 },
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref executed); return Task.FromResult(1); },
            },
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          scheduler.RunAsync(new WorkSubmission<int> { Items = items }));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task RunAsync_WhenLatencySensitiveItemWaits_IsPreferredOverThroughput()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(1, 1, 1, 1, 1, 1));
        var startOrder = new List<long>();
        var gate = new object();
        var items = new[]
        {
            new WorkItem<long>
            {
                StableOrder = 0,
                EstimatedCost = 9999,
                Priority = WorkPriority.Throughput,
                ExecuteAsync = (_, _) => { lock (gate) { startOrder.Add(0); } return Task.FromResult(0L); },
            },
            new WorkItem<long>
            {
                StableOrder = 1,
                EstimatedCost = 1,
                Priority = WorkPriority.LatencySensitive,
                ExecuteAsync = (_, _) => { lock (gate) { startOrder.Add(1); } return Task.FromResult(1L); },
            },
        };

        await scheduler.RunAsync(new WorkSubmission<long> { Items = items });

        Assert.Equal(new long[] { 1, 0 }, startOrder);
    }
}
