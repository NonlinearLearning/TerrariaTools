using System.Collections.Concurrent;
using NL.Concurrency;
using Xunit;

namespace RoslynPrototype.PerformanceTests.Concurrency;

public sealed class ConcurrencyPoolContractTests
{
    [Fact]
    public async Task SelectOrderedAsync_WhenWorkCompletesOutOfOrder_ReturnsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var sources = new[] { 0, 1, 2 };

        var results = await pool.SelectOrderedAsync(
          sources,
          maxDegreeOfParallelism: 3,
          async (source, _, _) =>
          {
              await Task.Delay((2 - source) * 10);
              return source;
          });

        Assert.Equal(sources, results);
    }

    [Fact]
    public async Task ForEachAsync_WhenParallelismIsBounded_DoesNotExceedRequestedDegree()
    {
        var pool = new BoundedConcurrencyPool();
        var active = 0;
        var peak = 0;
        var firstWindowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWindow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = pool.ForEachAsync(
          new[] { 0, 1, 2, 3 },
          maxDegreeOfParallelism: 2,
          async (_, _, _) =>
          {
              var current = Interlocked.Increment(ref active);
              InterlockedExtensions.Max(ref peak, current);
              if (current == 2)
              {
                  firstWindowStarted.TrySetResult();
              }

              try
              {
                  await releaseFirstWindow.Task;
              }
              finally
              {
                  Interlocked.Decrement(ref active);
              }
          });

        await firstWindowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFirstWindow.TrySetResult();
        await runTask;
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task SelectOrderedAsync_WhenWorkItemBlocksBeforeItsFirstAwait_StartsTheRestOfTheWorkerWindow()
    {
        var pool = new BoundedConcurrencyPool();
        var firstWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runTask = pool.SelectOrderedAsync(
          new[] { 0, 1 },
          maxDegreeOfParallelism: 2,
          async (source, _, _) =>
          {
              if (source == 0)
              {
                  firstWorkStarted.TrySetResult();
                  releaseFirstWork.Task.GetAwaiter().GetResult();
              }
              else
              {
                  secondWorkStarted.TrySetResult();
              }

              await Task.Yield();
              return source;
          });

        try
        {
            await firstWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondWorkStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            releaseFirstWork.TrySetResult();
            await runTask;
        }
    }

    [Fact]
    public async Task SelectOrderedAsync_WhenWorkItemFails_DoesNotStartQueuedWorkAfterTheFailure()
    {
        var pool = new BoundedConcurrencyPool();
        var started = new ConcurrentQueue<int>();
        var secondWorkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = pool.SelectOrderedAsync(
          new[] { 0, 1, 2, 3, 4 },
          maxDegreeOfParallelism: 2,
          async (source, _, cancellationToken) =>
          {
              started.Enqueue(source);
              if (source == 0)
              {
                  await secondWorkStarted.Task;
                  throw new InvalidOperationException("Synthetic work-item failure.");
              }

              if (source == 1)
              {
                  secondWorkStarted.TrySetResult();
                  await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
              }

              return source;
          });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runTask);
        Assert.DoesNotContain(started, source => source >= 2);
    }

    [Fact]
    public void CommitOrdered_WhenWorkersFinishOutOfOrder_CommitsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var committed = new List<int>();

        pool.CommitOrdered(
          new[] { 0, 1, 2 },
          new ConcurrencyWindowOptions(3),
          (source, _) => source,
          (result, _) => committed.Add(result));

        Assert.Equal(new[] { 0, 1, 2 }, committed);
    }

    [Fact]
    public void CommitTwoStageOrdered_WhenCollectAndSolveRunInWorkers_PreparesAndCommitsInputOrder()
    {
        var pool = new BoundedConcurrencyPool();
        var prepared = new List<int>();
        var committed = new List<int>();

        pool.CommitTwoStageOrdered(
          new[] { 0, 1, 2 },
          new ConcurrencyWindowOptions(3),
          (source, _) => source,
          (collected, index) =>
          {
              prepared.Add(index);
              return collected * 2;
          },
          (value, _) => value + 1,
          (result, _) => committed.Add(result));

        Assert.Equal(new[] { 0, 1, 2 }, prepared);
        Assert.Equal(new[] { 1, 3, 5 }, committed);
    }

    [Fact]
    public async Task RunDependencyGraphAsync_WhenDependenciesComplete_ExposesTheirResultsToDependentWork()
    {
        var pool = new BoundedConcurrencyPool();
        var workItems = new[]
        {
            new DependencyWorkItem<string, int>("A", Array.Empty<string>(), (_, _) => Task.FromResult(2)),
            new DependencyWorkItem<string, int>("B", Array.Empty<string>(), (_, _) => Task.FromResult(3)),
            new DependencyWorkItem<string, int>(
              "C",
              new[] { "A", "B" },
              (dependencies, _) => Task.FromResult(dependencies["A"] + dependencies["B"]))
        };

        var result = await pool.RunDependencyGraphAsync(
          workItems,
          maxDegreeOfParallelism: 2,
          StringComparer.Ordinal);

        Assert.Equal(5, result.Results["C"]);
        Assert.Equal(2, result.PeakConcurrentWorkItemCount);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (candidate <= current || Interlocked.CompareExchange(ref target, candidate, current) == current)
                {
                    return;
                }
            }
        }
    }
}
