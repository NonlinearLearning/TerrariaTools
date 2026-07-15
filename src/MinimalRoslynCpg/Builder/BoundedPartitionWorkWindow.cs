namespace MinimalRoslynCpg.Builder;

internal static class BoundedPartitionWorkWindow
{
  private sealed record CompletedWorkItem<TResult>(int Order, TResult Result);

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

  public static int RunOrdered<TInput, TResult>(
    IReadOnlyList<TInput> inputs,
    int maxDegreeOfParallelism,
    Func<TInput, int, TResult> workItem,
    Action<TResult, int> commit,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(inputs);
    ArgumentNullException.ThrowIfNull(workItem);
    ArgumentNullException.ThrowIfNull(commit);

    if (inputs.Count == 0)
    {
      return 0;
    }

    var workerCount = Math.Min(inputs.Count, Math.Max(1, maxDegreeOfParallelism));
    var activeWorkers = new List<Task<CompletedWorkItem<TResult>>>(workerCount);
    var completedResults = new Dictionary<int, TResult>();
    var nextOrderToSchedule = 0;
    var nextOrderToCommit = 0;
    var peakBufferedResultCount = 0;

    while (nextOrderToCommit < inputs.Count)
    {
      while (nextOrderToSchedule < inputs.Count &&
             activeWorkers.Count + completedResults.Count < workerCount)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var order = nextOrderToSchedule;
        nextOrderToSchedule += 1;
        activeWorkers.Add(Task.Run(() =>
        {
          cancellationToken.ThrowIfCancellationRequested();
          return new CompletedWorkItem<TResult>(order, workItem(inputs[order], order));
        }, cancellationToken));
      }

      if (activeWorkers.Count == 0)
      {
        throw new InvalidOperationException("The ordered work window stopped before all results were committed.");
      }

      var completedTask = Task.WhenAny(activeWorkers).GetAwaiter().GetResult();
      activeWorkers.Remove(completedTask);
      try
      {
        var completedWorkItem = completedTask.GetAwaiter().GetResult();
        completedResults.Add(completedWorkItem.Order, completedWorkItem.Result);
        peakBufferedResultCount = Math.Max(peakBufferedResultCount, completedResults.Count);
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
        commit(nextResult, nextOrderToCommit);
        nextOrderToCommit += 1;
      }
    }

    return peakBufferedResultCount;
  }
}
