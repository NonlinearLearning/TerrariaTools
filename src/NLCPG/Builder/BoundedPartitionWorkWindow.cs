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
}
