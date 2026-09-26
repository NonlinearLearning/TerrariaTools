using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Concurrency;

public sealed class WorkSchedulerFailureTests
{
    [Fact]
    public async Task RunAsync_WhenOneItemThrows_ThrowsAndCancelsRemaining()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(1, 1, 1, 1, 1, 1));
        var ranAfterFailure = 0;
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                ExecuteAsync = (_, _) => throw new InvalidOperationException("boom"),
            },
            new WorkItem<int>
            {
                StableOrder = 1,
                Dependencies = new long[] { 0 },
                ExecuteAsync = (_, _) => { Interlocked.Increment(ref ranAfterFailure); return Task.FromResult(1); },
            },
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          scheduler.RunAsync(new WorkSubmission<int> { Items = items }));

        Assert.Equal("boom", error.Message);
        Assert.Equal(0, Volatile.Read(ref ranAfterFailure));
    }

    [Fact]
    public async Task RunAsync_WhenCallerTokenIsCanceled_ThrowsOperationCanceled()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var items = new[]
        {
            new WorkItem<int>
            {
                StableOrder = 0,
                ExecuteAsync = async (_, token) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    return 0;
                },
            },
        };

        var run = scheduler.RunAsync(new WorkSubmission<int> { Items = items }, cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task RunAsync_WhenPreviousSubmissionFailed_SchedulerRemainsUsable()
    {
        await using var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        var failing = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => throw new InvalidOperationException("first") },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
          scheduler.RunAsync(new WorkSubmission<int> { Items = failing }));

        var healthy = new[]
        {
            new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(11) },
            new WorkItem<int> { StableOrder = 1, ExecuteAsync = (_, _) => Task.FromResult(31) },
        };

        var results = await scheduler.RunAsync(new WorkSubmission<int> { Items = healthy });

        Assert.Equal(new[] { 11, 31 }, results);
    }

    [Fact]
    public async Task DisposeAsync_WhenInFlightItemsExist_CompletesThemBeforeReturning()
    {
        var scheduler = new WorkScheduler(new WorkSchedulerOptions(4, 4, 4, 4, 4, 4));
        var completed = 0;
        var items = Enumerable.Range(0, 8).Select(index => new WorkItem<int>
        {
            StableOrder = index,
            ExecuteAsync = async (_, _) =>
            {
                await Task.Delay(10);
                Interlocked.Increment(ref completed);
                return index;
            },
        }).ToArray();

        var run = scheduler.RunAsync(new WorkSubmission<int> { Items = items });
        await scheduler.DisposeAsync();
        var results = await run;

        Assert.Equal(8, Volatile.Read(ref completed));
        Assert.Equal(Enumerable.Range(0, 8), results);
    }

    [Fact]
    public async Task RunAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var scheduler = new WorkScheduler(new WorkSchedulerOptions(2, 2, 2, 2, 2, 2));
        await scheduler.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          scheduler.RunAsync(new WorkSubmission<int>
          {
              Items = new[] { new WorkItem<int> { StableOrder = 0, ExecuteAsync = (_, _) => Task.FromResult(1) } },
          }));
    }
}
