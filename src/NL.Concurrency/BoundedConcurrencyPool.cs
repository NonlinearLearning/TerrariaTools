using System.Runtime.ExceptionServices;

namespace NL.Concurrency;

public sealed class BoundedConcurrencyPool : IConcurrencyPool
{
    public Task<IReadOnlyList<TResult>> SelectOrderedAsync<TResult>(
        int itemCount,
        int maxDegreeOfParallelism,
        Func<int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }

        return SelectOrderedAsync(
          Enumerable.Range(0, itemCount).ToArray(),
          maxDegreeOfParallelism,
          (index, _, token) => workItem(index, token),
          cancellationToken);
    }

    public async Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(workItem);

        if (sources.Count == 0)
        {
            return Array.Empty<TResult>();
        }

        using var workItemCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var results = new TResult[sources.Count];
        var nextIndex = -1;
        ExceptionDispatchInfo? workItemFailure = null;
        var workItemStartGate = new object();
        var workerCount = Math.Min(sources.Count, Math.Max(1, maxDegreeOfParallelism));
        var workers = new Task[workerCount];
        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            workers[workerIndex] = Task.Run(
              async () =>
              {
                  while (true)
                  {
                      Task<TResult> workItemTask;
                      int index;
                      lock (workItemStartGate)
                      {
                          if (workItemFailure is not null)
                          {
                              return;
                          }

                          index = Interlocked.Increment(ref nextIndex);
                          if (index >= sources.Count)
                          {
                              return;
                          }

                          cancellationToken.ThrowIfCancellationRequested();
                          // Register the work while holding the gate, but invoke caller code on a
                          // separate task. An async delegate may block before its first await.
                          workItemTask = Task.Run(
                            async () =>
                            {
                                workItemCancellation.Token.ThrowIfCancellationRequested();
                                return await workItem(
                                  sources[index],
                                  index,
                                  workItemCancellation.Token).ConfigureAwait(false);
                            },
                            CancellationToken.None);
                      }

                      try
                      {
                          results[index] = await workItemTask;
                      }
                      catch (Exception exception)
                      {
                          var shouldCancelWorkItems = false;
                          lock (workItemStartGate)
                          {
                              if (workItemFailure is null)
                              {
                                  workItemFailure = ExceptionDispatchInfo.Capture(exception);
                                  shouldCancelWorkItems = true;
                              }
                          }

                          if (shouldCancelWorkItems)
                          {
                              workItemCancellation.Cancel();
                          }

                          return;
                      }
                  }
              },
              cancellationToken);
        }

        await Task.WhenAll(workers);
        Volatile.Read(ref workItemFailure)?.Throw();
        return results;
    }

    public void CommitOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TResult> workItem,
        Action<TResult, int> commit,
        Func<TResult, int>? retainedRecordCount = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(commit);

        if (sources.Count == 0)
        {
            return;
        }

        var activeWorkers = new List<Task<CompletedWorkItem<TResult>>>(options.EffectiveMaxDegreeOfParallelism);
        var completedResults = new Dictionary<int, CompletedWorkItem<TResult>>();
        var nextOrderToSchedule = 0;
        var nextOrderToCommit = 0;
        var completedRecordCount = 0;

        while (nextOrderToCommit < sources.Count)
        {
            while (nextOrderToSchedule < sources.Count &&
                   activeWorkers.Count < options.EffectiveMaxDegreeOfParallelism &&
                   completedResults.Count < options.EffectiveReorderAllowance &&
                   completedRecordCount < options.EffectiveMaxCompletedRecordCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var order = nextOrderToSchedule;
                nextOrderToSchedule++;
                activeWorkers.Add(Task.Run(
                  () =>
                  {
                      cancellationToken.ThrowIfCancellationRequested();
                      var result = workItem(sources[order], order);
                      return new CompletedWorkItem<TResult>(
                        order,
                        result,
                        Math.Max(0, retainedRecordCount?.Invoke(result) ?? 1));
                  },
                  cancellationToken));
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
                completedResults.Add(completedWorkItem.Order, completedWorkItem);
                completedRecordCount += completedWorkItem.RetainedRecordCount;
            }
            catch
            {
                WaitForWorkers(activeWorkers);
                throw;
            }

            while (completedResults.Remove(nextOrderToCommit, out var nextResult))
            {
                cancellationToken.ThrowIfCancellationRequested();
                commit(nextResult.Result, nextOrderToCommit);
                completedRecordCount -= nextResult.RetainedRecordCount;
                nextOrderToCommit++;
            }
        }
    }

    public void CommitTwoStageOrdered<TSource, TCollected, TPrepared, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TCollected> collect,
        Func<TCollected, int, TPrepared> prepare,
        Func<TPrepared, int, TResult> solve,
        Action<TResult, int> commit,
        Func<TCollected, int>? collectedRetainedRecordCount = null,
        Func<TResult, int>? resultRetainedRecordCount = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(commit);

        if (sources.Count == 0)
        {
            return;
        }

        var activeCollections = new List<Task<CompletedWorkItem<TCollected>>>(options.EffectiveMaxDegreeOfParallelism);
        var activeSolves = new List<Task<CompletedWorkItem<TResult>>>(options.EffectiveMaxDegreeOfParallelism);
        var completedCollections = new Dictionary<int, CompletedWorkItem<TCollected>>();
        var completedResults = new Dictionary<int, CompletedWorkItem<TResult>>();
        var nextOrderToSchedule = 0;
        var nextOrderToPrepare = 0;
        var nextOrderToCommit = 0;
        var completedRecordCount = 0;

        while (nextOrderToCommit < sources.Count)
        {
            while (nextOrderToSchedule < sources.Count &&
                   activeCollections.Count + activeSolves.Count < options.EffectiveMaxDegreeOfParallelism &&
                   completedCollections.Count < options.EffectiveReorderAllowance &&
                   completedResults.Count < options.EffectiveReorderAllowance &&
                   completedRecordCount < options.EffectiveMaxCompletedRecordCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var order = nextOrderToSchedule;
                nextOrderToSchedule++;
                activeCollections.Add(Task.Run(
                  () =>
                  {
                      cancellationToken.ThrowIfCancellationRequested();
                      var collected = collect(sources[order], order);
                      return new CompletedWorkItem<TCollected>(
                        order,
                        collected,
                        Math.Max(0, collectedRetainedRecordCount?.Invoke(collected) ?? 1));
                  },
                  cancellationToken));
            }

            while (activeCollections.Count + activeSolves.Count < options.EffectiveMaxDegreeOfParallelism &&
                   completedCollections.Remove(nextOrderToPrepare, out var collectedWorkItem))
            {
                completedRecordCount -= collectedWorkItem.RetainedRecordCount;
                var order = collectedWorkItem.Order;
                var prepared = prepare(collectedWorkItem.Result, order);
                nextOrderToPrepare++;
                activeSolves.Add(Task.Run(
                  () =>
                  {
                      cancellationToken.ThrowIfCancellationRequested();
                      var result = solve(prepared, order);
                      return new CompletedWorkItem<TResult>(
                        order,
                        result,
                        Math.Max(0, resultRetainedRecordCount?.Invoke(result) ?? 1));
                  },
                  cancellationToken));
            }

            while (completedResults.Remove(nextOrderToCommit, out var resultWorkItem))
            {
                cancellationToken.ThrowIfCancellationRequested();
                commit(resultWorkItem.Result, nextOrderToCommit);
                completedRecordCount -= resultWorkItem.RetainedRecordCount;
                nextOrderToCommit++;
            }

            if (nextOrderToCommit == sources.Count)
            {
                return;
            }

            var activeTasks = activeCollections.Cast<Task>().Concat(activeSolves).ToArray();
            if (activeTasks.Length == 0)
            {
                if (nextOrderToSchedule < sources.Count || completedCollections.ContainsKey(nextOrderToPrepare))
                {
                    continue;
                }

                throw new InvalidOperationException("The two-stage ordered work window stopped before all results were committed.");
            }

            var completedTask = Task.WhenAny(activeTasks).GetAwaiter().GetResult();
            var completedCollectionTask = activeCollections.FirstOrDefault(task => ReferenceEquals(task, completedTask));
            if (completedCollectionTask is not null)
            {
                activeCollections.Remove(completedCollectionTask);
                try
                {
                    var collectedWorkItem = completedCollectionTask.GetAwaiter().GetResult();
                    completedCollections.Add(collectedWorkItem.Order, collectedWorkItem);
                    completedRecordCount += collectedWorkItem.RetainedRecordCount;
                }
                catch
                {
                    WaitForWorkers(activeCollections.Cast<Task>().Concat(activeSolves));
                    throw;
                }
            }
            else
            {
                var completedSolveTask = (Task<CompletedWorkItem<TResult>>)completedTask;
                activeSolves.Remove(completedSolveTask);
                try
                {
                    var resultWorkItem = completedSolveTask.GetAwaiter().GetResult();
                    completedResults.Add(resultWorkItem.Order, resultWorkItem);
                    completedRecordCount += resultWorkItem.RetainedRecordCount;
                }
                catch
                {
                    WaitForWorkers(activeCollections.Cast<Task>().Concat(activeSolves));
                    throw;
                }
            }
        }
    }

    public async Task ForEachAsync<TSource>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        await SelectOrderedAsync(
          sources,
          maxDegreeOfParallelism,
          async (source, index, token) =>
          {
              await workItem(source, index, token);
              return true;
          },
          cancellationToken);
    }

    public async Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(
        IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems,
        int maxDegreeOfParallelism,
        IComparer<TNode> readyOrder,
        CancellationToken cancellationToken = default)
        where TNode : notnull
    {
        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentNullException.ThrowIfNull(readyOrder);

        var workItemsByNode = workItems.ToDictionary(workItem => workItem.Node);
        if (workItemsByNode.Count != workItems.Count)
        {
            throw new InvalidOperationException("Dependency work item nodes must be unique.");
        }

        var remainingDependencies = new Dictionary<TNode, int>();
        var downstreamNodes = workItemsByNode.Keys.ToDictionary(node => node, _ => new List<TNode>());
        foreach (var workItem in workItems)
        {
            var dependencies = workItem.Dependencies.Distinct().ToList();
            if (dependencies.Any(dependency => !workItemsByNode.ContainsKey(dependency)))
            {
                throw new InvalidOperationException("Dependency work items must reference known nodes.");
            }

            remainingDependencies.Add(workItem.Node, dependencies.Count);
            foreach (var dependency in dependencies)
            {
                downstreamNodes[dependency].Add(workItem.Node);
            }
        }

        var ready = new SortedSet<TNode>(readyOrder);
        foreach (var workItem in workItems.Where(workItem => remainingDependencies[workItem.Node] == 0))
        {
            ready.Add(workItem.Node);
        }

        var results = new Dictionary<TNode, TResult>();
        var running = new Dictionary<Task<DependencyCompletion<TNode, TResult>>, TNode>();
        var degree = Math.Max(1, maxDegreeOfParallelism);
        var peakReadyWorkItemCount = ready.Count;
        var peakConcurrentWorkItemCount = 0;

        while (ready.Count > 0 || running.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (ready.Count > 0 && running.Count < degree)
            {
                var node = ready.Min!;
                ready.Remove(node);
                var workItem = workItemsByNode[node];
                var dependencyResults = workItem.Dependencies
                  .Distinct()
                  .ToDictionary(dependency => dependency, dependency => results[dependency]);
                var task = Task.Run(
                  async () => new DependencyCompletion<TNode, TResult>(
                    node,
                    await workItem.ExecuteAsync(dependencyResults, cancellationToken)),
                  cancellationToken);
                running.Add(task, node);
                peakConcurrentWorkItemCount = Math.Max(peakConcurrentWorkItemCount, running.Count);
            }

            var completedTask = await Task.WhenAny(running.Keys);
            var completedNode = running[completedTask];
            running.Remove(completedTask);
            DependencyCompletion<TNode, TResult> completed;
            try
            {
                completed = await completedTask;
            }
            catch
            {
                try
                {
                    await Task.WhenAll(running.Keys);
                }
                catch
                {
                }

                throw;
            }

            results.Add(completedNode, completed.Result);
            foreach (var downstreamNode in downstreamNodes[completedNode])
            {
                remainingDependencies[downstreamNode]--;
                if (remainingDependencies[downstreamNode] == 0)
                {
                    ready.Add(downstreamNode);
                    peakReadyWorkItemCount = Math.Max(peakReadyWorkItemCount, ready.Count);
                }
            }
        }

        return new DependencyExecutionResult<TNode, TResult>(
          results,
          peakReadyWorkItemCount,
          peakConcurrentWorkItemCount);
    }

    private sealed record CompletedWorkItem<TResult>(int Order, TResult Result, int RetainedRecordCount);

    private sealed record DependencyCompletion<TNode, TResult>(TNode Node, TResult Result)
        where TNode : notnull;

    private static void WaitForWorkers(IEnumerable<Task> workers)
    {
        try
        {
            Task.WhenAll(workers).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}
