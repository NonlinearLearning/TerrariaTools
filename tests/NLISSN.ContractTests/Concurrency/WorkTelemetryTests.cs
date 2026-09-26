using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkTelemetryTests
{
    [Fact]
    public async Task RunAsync_WhenSinkAttached_RecordsOneEntryWithFilledFields()
    {
        var collector = new WorkTelemetryCollector();
        await using var scheduler = new WorkScheduler(
          new WorkSchedulerOptions(4, 4, 4, 4, 4, 4),
          collector);
        var items = Enumerable.Range(0, 5).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            ExecuteAsync = (_, _) => Task.FromResult(index),
        }).ToArray();

        await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = items,
            Category = WorkCategories.Cpg,
        });

        var record = Assert.Single(collector.Records);
        Assert.Equal(WorkCategories.Cpg, record.Category);
        Assert.Equal(5, record.InputCount);
        Assert.Equal(4, record.MaxConcurrency);
        Assert.True(record.PeakActiveCount >= 1);
        Assert.True(record.PeakReadyCount >= 1);
        Assert.False(record.WasCanceled);
        Assert.True(record.Elapsed >= TimeSpan.Zero);
        Assert.True(record.MaxQueueWait >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_WhenSubmissionFails_RecordsWasCanceled()
    {
        var collector = new WorkTelemetryCollector();
        await using var scheduler = new WorkScheduler(
          new WorkSchedulerOptions(2, 2, 2, 2, 2, 2),
          collector);
        var items = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => throw new InvalidOperationException("x") },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
          scheduler.RunAsync(new WorkSubmission<int> { Items = items }));

        Assert.True(Assert.Single(collector.Records).WasCanceled);
    }

    [Fact]
    public async Task RunAsync_WhenSinkThrows_SubmissionStillSucceeds()
    {
        await using var scheduler = new WorkScheduler(
          new WorkSchedulerOptions(2, 2, 2, 2, 2, 2),
          new ThrowingSink());
        var items = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(5) },
        };

        // fail-open：遥测写入失败不得改变调度与成功语义。
        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = items });

        Assert.Equal(new[] { 5 }, results);
    }

    [Fact]
    public async Task RunAsync_WhenNoSinkAttached_DoesNotThrow()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));

        var results = await scheduler.RunAsync(new WorkSubmission<int>
        {
            Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(9) } },
        });

        Assert.Equal(new[] { 9 }, results);
    }

    private sealed class ThrowingSink : IWorkTelemetrySink
    {
        public void Record(WorkTelemetry telemetry)
        {
            throw new InvalidOperationException("telemetry sink failed");
        }
    }
}
