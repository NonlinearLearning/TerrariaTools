using System.Diagnostics;

namespace MinimalRoslynCpg.Builder;

internal static class BoundedPartitionWorkWindow
{
  private sealed record CompletedWorkItem<TResult>(
    int Order,
    TResult Result,
    int RetainedRecordCount,
    long CompletedTimestamp);

  public static async Task<TResult[]> RunAsync<TInput, TResult>(
    IReadOnlyList<TInput> inputs,
    int maxDegreeOfParallelism,
    Func<TInput, int, TResult> workItem)
  {
    ArgumentNullException.ThrowIfNull(inputs);
    ArgumentNullException.ThrowIfNull(workItem);

    if (inputs.Count == 0)
    {
      return Array.Empty<TResult>();
    }

    var results = new TResult[inputs.Count];
    var nextIndex = -1;
    var workerCount = Math.Min(inputs.Count, Math.Max(1, maxDegreeOfParallelism));
    var workers = new Task[workerCount];
    for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
    {
      workers[workerIndex] = Task.Run(() =>
      {
        while (true)
        {
          var index = Interlocked.Increment(ref nextIndex);
          if (index >= inputs.Count)
          {
            return;
          }

          results[index] = workItem(inputs[index], index);
        }
      });
    }

    await Task.WhenAll(workers);
    return results;
  }

  public static RoslynCpgOrderedWorkWindowTelemetry RunOrdered<TInput, TResult>(
    IReadOnlyList<TInput> inputs,
    int maxDegreeOfParallelism,
    Func<TInput, int, TResult> workItem,
    Action<TResult, int> commit,
    CancellationToken cancellationToken = default,
    Func<TResult, int>? retainedRecordCount = null,
    int? reorderAllowance = null,
    int maxCompletedRecordCount = int.MaxValue)
  {
    ArgumentNullException.ThrowIfNull(inputs);
    ArgumentNullException.ThrowIfNull(workItem);
    ArgumentNullException.ThrowIfNull(commit);

    if (inputs.Count == 0)
    {
      return RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
    }

    var workerCount = Math.Min(inputs.Count, Math.Max(1, maxDegreeOfParallelism));
    var effectiveReorderAllowance = Math.Max(0, reorderAllowance ?? workerCount);
    var effectiveMaxCompletedRecordCount = Math.Max(1, maxCompletedRecordCount);
    var activeWorkers = new List<Task<CompletedWorkItem<TResult>>>(workerCount);
    var completedResults = new Dictionary<int, CompletedWorkItem<TResult>>();
    var nextOrderToSchedule = 0;
    var nextOrderToCommit = 0;
    var activeWorkerPeak = 0;
    var completedButUncommittedPeak = 0;
    var completedRecordCountPeak = 0;
    var completedRecordCount = 0;
    var commitWaitMilliseconds = 0L;
    var windowBlockedMilliseconds = 0L;

    while (nextOrderToCommit < inputs.Count)
    {
      while (nextOrderToSchedule < inputs.Count &&
             activeWorkers.Count < workerCount &&
             completedResults.Count < effectiveReorderAllowance &&
             completedRecordCount < effectiveMaxCompletedRecordCount)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var order = nextOrderToSchedule;
        nextOrderToSchedule += 1;
        activeWorkers.Add(Task.Run(() =>
        {
          cancellationToken.ThrowIfCancellationRequested();
          var result = workItem(inputs[order], order);
          return new CompletedWorkItem<TResult>(
            order,
            result,
            Math.Max(0, retainedRecordCount?.Invoke(result) ?? 1),
            Stopwatch.GetTimestamp());
        }, cancellationToken));
        activeWorkerPeak = Math.Max(activeWorkerPeak, activeWorkers.Count);
      }

      if (activeWorkers.Count == 0)
      {
        throw new InvalidOperationException("The ordered work window stopped before all results were committed.");
      }

      var windowBlockedStart = nextOrderToSchedule < inputs.Count &&
        (completedResults.Count >= effectiveReorderAllowance ||
          completedRecordCount >= effectiveMaxCompletedRecordCount)
          ? Stopwatch.GetTimestamp()
          : 0;
      var completedTask = Task.WhenAny(activeWorkers).GetAwaiter().GetResult();
      if (windowBlockedStart != 0)
      {
        windowBlockedMilliseconds += (long)Stopwatch.GetElapsedTime(windowBlockedStart).TotalMilliseconds;
      }

      activeWorkers.Remove(completedTask);
      try
      {
        var completedWorkItem = completedTask.GetAwaiter().GetResult();
        completedResults.Add(completedWorkItem.Order, completedWorkItem);
        completedButUncommittedPeak = Math.Max(completedButUncommittedPeak, completedResults.Count);
        completedRecordCount += completedWorkItem.RetainedRecordCount;
        completedRecordCountPeak = Math.Max(completedRecordCountPeak, completedRecordCount);
      }
      catch
      {
        try
        {
          Task.WhenAll(activeWorkers).GetAwaiter().GetResult();
        }
        catch
        {
          // The original worker failure determines the build outcome.
        }

        throw;
      }

      while (completedResults.Remove(nextOrderToCommit, out var nextResult))
      {
        cancellationToken.ThrowIfCancellationRequested();
        commitWaitMilliseconds += (long)Stopwatch.GetElapsedTime(nextResult.CompletedTimestamp).TotalMilliseconds;
        commit(nextResult.Result, nextOrderToCommit);
        completedRecordCount -= nextResult.RetainedRecordCount;
        nextOrderToCommit += 1;
      }
    }

    return new RoslynCpgOrderedWorkWindowTelemetry(
      ActiveWorkerPeak: activeWorkerPeak,
      CompletedButUncommittedPeak: completedButUncommittedPeak,
      CompletedRecordCountPeak: completedRecordCountPeak,
      CommitWaitMilliseconds: commitWaitMilliseconds,
      WindowBlockedMilliseconds: windowBlockedMilliseconds);
  }
}
