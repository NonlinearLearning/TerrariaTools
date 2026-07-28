using System.Diagnostics;

namespace NLCPG.Builder;

/// 为可并发分析、必须按源顺序提交的构图阶段提供有界工作窗口。
/// 工作线程只产生结果；<c>commit</c> 始终由调用线程按输入顺序执行，避免并发修改图状态。
internal static class BoundedPartitionWorkWindow
{
    private sealed record CompletedWorkItem<TResult>(int Order, TResult Result, int RetainedRecordCount, long CompletedTimestamp);

    // 并发执行分区工作，并按输入序号返回全部结果。
    public static async Task<TResult[]> RunAsync<TInput, TResult>(IReadOnlyList<TInput> inputs, int maxDegreeOfParallelism, Func<TInput, int, TResult> workItem)
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

    // 并发计算各分区结果，并按原始顺序在调用线程提交。
    public static void RunOrdered<TInput, TResult>(IReadOnlyList<TInput> inputs, int maxDegreeOfParallelism, Func<TInput, int, TResult> workItem, Action<TResult, int> commit, CancellationToken cancellationToken = default, Func<TResult, int>? retainedRecordCount = null, int? reorderAllowance = null, int maxCompletedRecordCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(commit);

        if (inputs.Count == 0)
        {
            return;
        }

        var workerCount = Math.Min(inputs.Count, Math.Max(1, maxDegreeOfParallelism));
        var effectiveReorderAllowance = Math.Max(0, reorderAllowance ?? workerCount);
        var effectiveMaxCompletedRecordCount = Math.Max(1, maxCompletedRecordCount);
        var activeWorkers = new List<Task<CompletedWorkItem<TResult>>>(workerCount);
        var completedResults = new Dictionary<int, CompletedWorkItem<TResult>>();
        var nextOrderToSchedule = 0;
        var nextOrderToCommit = 0;
        var completedRecordCount = 0;

        while (nextOrderToCommit < inputs.Count)
        {
            // 任一上限被占满时暂停继续调度，防止靠前的慢分区让后续完成结果无限堆积。
            while (nextOrderToSchedule < inputs.Count && activeWorkers.Count < workerCount && completedResults.Count < effectiveReorderAllowance && completedRecordCount < effectiveMaxCompletedRecordCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var order = nextOrderToSchedule;
                nextOrderToSchedule += 1;
                activeWorkers.Add(Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = workItem(inputs[order], order);
                    return new CompletedWorkItem<TResult>(order, result, Math.Max(0, retainedRecordCount?.Invoke(result) ?? 1), Stopwatch.GetTimestamp());
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
                completedResults.Add(completedWorkItem.Order, completedWorkItem);
                completedRecordCount += completedWorkItem.RetainedRecordCount;
            }
            catch
            {
                try
                {
                    Task.WhenAll(activeWorkers).GetAwaiter().GetResult();
                }
                catch
                {
                    // 以最先观察到的工作线程异常为准；这里只等待其余任务退出，避免遗留后台任务。
                }

                throw;
            }

            while (completedResults.Remove(nextOrderToCommit, out var nextResult))
            {
                cancellationToken.ThrowIfCancellationRequested();
                commit(nextResult.Result, nextOrderToCommit);
                completedRecordCount -= nextResult.RetainedRecordCount;
                nextOrderToCommit += 1;
            }
        }
    }

    // 采集与求解都可并行，但 prepare/commit 始终在调用线程按源序执行。
    public static void RunTwoStageOrdered<TInput, TCollected, TPrepared, TResult>(
      IReadOnlyList<TInput> inputs,
      int maxDegreeOfParallelism,
      Func<TInput, int, TCollected> collect,
      Func<TCollected, int, TPrepared> prepare,
      Func<TPrepared, int, TResult> solve,
      Action<TResult, int> commit,
      CancellationToken cancellationToken = default,
      Func<TCollected, int>? collectedRetainedRecordCount = null,
      Func<TResult, int>? resultRetainedRecordCount = null,
      int? reorderAllowance = null,
      int maxCompletedRecordCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(commit);

        if (inputs.Count == 0)
        {
            return;
        }

        var workerCount = Math.Min(inputs.Count, Math.Max(1, maxDegreeOfParallelism));
        var effectiveReorderAllowance = Math.Max(0, reorderAllowance ?? workerCount);
        var effectiveMaxCompletedRecordCount = Math.Max(1, maxCompletedRecordCount);
        var activeCollections = new List<Task<CompletedWorkItem<TCollected>>>(workerCount);
        var activeSolves = new List<Task<CompletedWorkItem<TResult>>>(workerCount);
        var completedCollections = new Dictionary<int, CompletedWorkItem<TCollected>>();
        var completedResults = new Dictionary<int, CompletedWorkItem<TResult>>();
        var nextOrderToSchedule = 0;
        var nextOrderToPrepare = 0;
        var nextOrderToCommit = 0;
        var completedRecordCount = 0;

        while (nextOrderToCommit < inputs.Count)
        {
            while (nextOrderToSchedule < inputs.Count &&
                   activeCollections.Count + activeSolves.Count < workerCount &&
                   completedCollections.Count < effectiveReorderAllowance &&
                   completedResults.Count < effectiveReorderAllowance &&
                   completedRecordCount < effectiveMaxCompletedRecordCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var order = nextOrderToSchedule;
                nextOrderToSchedule += 1;
                activeCollections.Add(Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var collected = collect(inputs[order], order);
                    return new CompletedWorkItem<TCollected>(
                      order,
                      collected,
                      Math.Max(0, collectedRetainedRecordCount?.Invoke(collected) ?? 1),
                      Stopwatch.GetTimestamp());
                }, cancellationToken));
            }

            while (activeCollections.Count + activeSolves.Count < workerCount &&
                   completedCollections.Remove(nextOrderToPrepare, out var collectedWorkItem))
            {
                completedRecordCount -= collectedWorkItem.RetainedRecordCount;
                var order = collectedWorkItem.Order;
                var prepared = prepare(collectedWorkItem.Result, order);
                nextOrderToPrepare += 1;
                activeSolves.Add(Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = solve(prepared, order);
                    return new CompletedWorkItem<TResult>(
                      order,
                      result,
                      Math.Max(0, resultRetainedRecordCount?.Invoke(result) ?? 1),
                      Stopwatch.GetTimestamp());
                }, cancellationToken));
            }

            while (completedResults.Remove(nextOrderToCommit, out var resultWorkItem))
            {
                cancellationToken.ThrowIfCancellationRequested();
                commit(resultWorkItem.Result, resultWorkItem.Order);
                completedRecordCount -= resultWorkItem.RetainedRecordCount;
                nextOrderToCommit += 1;
            }

            if (nextOrderToCommit == inputs.Count)
            {
                return;
            }

            var activeTasks = activeCollections.Cast<Task>()
              .Concat(activeSolves)
              .ToArray();
            if (activeTasks.Length == 0)
            {
                if (nextOrderToSchedule < inputs.Count ||
                    completedCollections.ContainsKey(nextOrderToPrepare))
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

    private static void WaitForWorkers(IEnumerable<Task> workers)
    {
        try
        {
            Task.WhenAll(workers).GetAwaiter().GetResult();
        }
        catch
        {
            // 保留第一个观察到的工作异常；这里只等待其余任务退出。
        }
    }
}
