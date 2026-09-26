using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace NL.Concurrency;

/// <summary>
/// 提供带有有序提交、依赖调度和遥测采集的受限并发执行池。
/// </summary>
public sealed class BoundedConcurrencyPool : IConcurrencyPool
{
    private readonly IConcurrencyPoolTelemetrySink? _telemetrySink;
    private readonly ConcurrencyAdmissionController? _admissionController;
    private readonly AsyncLocal<ConcurrencyAdmissionController.ConcurrencyAdmissionLease?> _currentAdmissionLease = new();

    /// <summary>
    /// 使用默认遥测和默认准入控制创建并发池。
    /// </summary>
    public BoundedConcurrencyPool()
      : this(null)
    {
    }

    /// <summary>
    /// 使用指定的遥测接收器创建并发池。
    /// </summary>
    public BoundedConcurrencyPool(IConcurrencyPoolTelemetrySink? telemetrySink)
      : this(telemetrySink, null)
    {
    }

    /// <summary>
    /// 使用指定的遥测接收器和准入控制器创建并发池。
    /// </summary>
    public BoundedConcurrencyPool(
        IConcurrencyPoolTelemetrySink? telemetrySink,
        ConcurrencyAdmissionController? admissionController)
    {
        _telemetrySink = telemetrySink;
        _admissionController = admissionController;
    }

    /// <summary>
    /// 在一次准入租约内执行长期 worker 操作。
    /// </summary>
    public async Task<TResult> ExecuteWithAdmissionAsync<TResult>(
        ConcurrencyAdmissionRequest request,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.FixedWorkers,
          request.ReservedItemCount,
          request.ReservedItemCount,
          request.WorkType);
        try
        {
            using var admissionLease = await AcquireAdmissionAsync(
              request,
              cancellationToken,
              telemetry).ConfigureAwait(false);
            using var admissionScope = PushAdmissionLease(admissionLease);
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            telemetry.MarkCanceled();
            throw;
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 按索引顺序并发执行异步工作项，并按输入顺序返回结果。
    /// </summary>
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

    /// <summary>
    /// 对输入源执行有序异步选择，在保持结果顺序的同时限制并发度。
    /// </summary>
    public async Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(workItem);

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.OrderedSelection,
          sources.Count,
          maxDegreeOfParallelism);

        if (sources.Count == 0)
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
            return Array.Empty<TResult>();
        }

        try
        {
            using var admissionLease = await AcquireAdmissionAsync(
              new ConcurrencyAdmissionRequest(ConcurrencyWorkType.Throughput),
              cancellationToken,
              telemetry).ConfigureAwait(false);
            using var admissionScope = PushAdmissionLease(admissionLease);
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
                          // 在持有门闩时先登记任务，但把调用方代码放到独立任务里执行。
                          // 异步委托可能在第一次 await 之前就发生阻塞。
                          workItemTask = Task.Run(
                            async () =>
                            {
                                workItemCancellation.Token.ThrowIfCancellationRequested();
                                telemetry.WorkItemStarted();
                                try
                                {
                                    return await workItem(
                                  sources[index],
                                  index,
                                  workItemCancellation.Token).ConfigureAwait(false);
                                }
                                finally
                                {
                                    telemetry.WorkItemCompleted();
                                }
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
        catch (OperationCanceledException)
        {
            telemetry.MarkCanceled();
            throw;
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 对处理器密集型工作项执行有序并发选择，并按输入顺序输出结果。
    /// </summary>
    public async Task<IReadOnlyList<TResult>> SelectCpuBoundOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, TResult> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(workItem);

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.CpuBoundOrderedSelection,
          sources.Count,
          maxDegreeOfParallelism);

        try
        {
            if (sources.Count == 0)
            {
                return Array.Empty<TResult>();
            }

            using var admissionLease = await AcquireAdmissionAsync(
              new ConcurrencyAdmissionRequest(ConcurrencyWorkType.Throughput),
              cancellationToken,
              telemetry).ConfigureAwait(false);
            using var admissionScope = PushAdmissionLease(admissionLease);
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
                  () =>
                  {
                      while (true)
                      {
                          int index;
                          lock (workItemStartGate)
                          {
                              if (workItemFailure is not null)
                              {
                                  return;
                              }

                              cancellationToken.ThrowIfCancellationRequested();
                              index = Interlocked.Increment(ref nextIndex);
                              if (index >= sources.Count)
                              {
                                  return;
                              }
                          }

                          try
                          {
                              workItemCancellation.Token.ThrowIfCancellationRequested();
                              telemetry.WorkItemStarted();
                              try
                              {
                                  results[index] = workItem(sources[index], index, workItemCancellation.Token);
                              }
                              finally
                              {
                                  telemetry.WorkItemCompleted();
                              }
                          }
                          catch (Exception exception)
                          {
                              if (exception is OperationCanceledException)
                              {
                                  telemetry.MarkCanceled();
                              }

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
                  CancellationToken.None);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            Volatile.Read(ref workItemFailure)?.Throw();
            cancellationToken.ThrowIfCancellationRequested();
            return results;
        }
        catch (OperationCanceledException)
        {
            telemetry.MarkCanceled();
            throw;
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 在受限重排窗口内并发计算结果，并按原始顺序串行提交。
    /// </summary>
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
        options.Validate();

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.OrderedCommit,
          sources.Count,
          options.EffectiveMaxDegreeOfParallelism,
          options.WorkType);
        if (sources.Count == 0)
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
            return;
        }

        try
        {
            using var admissionLease = AcquireAdmissionAsync(
              options.CreateAdmissionRequest(sources.Count),
              cancellationToken,
              telemetry).GetAwaiter().GetResult();
            using var admissionScope = PushAdmissionLease(admissionLease);
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
                       completedRecordCount < options.EffectiveMaxCompletedRecordCount &&
                       CanScheduleWithinWindow(nextOrderToSchedule, nextOrderToCommit, options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var order = nextOrderToSchedule;
                    nextOrderToSchedule++;
                    activeWorkers.Add(Task.Run(
                      () =>
                      {
                          cancellationToken.ThrowIfCancellationRequested();
                          telemetry.WorkItemStarted();
                          try
                          {
                              var result = workItem(sources[order], order);
                              return new CompletedWorkItem<TResult>(
                                order,
                                result,
                                Math.Max(0, retainedRecordCount?.Invoke(result) ?? 1));
                          }
                          finally
                          {
                              telemetry.WorkItemCompleted();
                          }
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
                    telemetry.CompletedBufferChanged(completedResults.Count);
                    telemetry.RetainedRecordCountChanged(completedRecordCount);
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
                    telemetry.CompletedBufferChanged(completedResults.Count);
                    telemetry.RetainedRecordCountChanged(completedRecordCount);
                    nextOrderToCommit++;
                }
            }
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 以采集、准备、求解、提交四个阶段执行两段式有序并发处理。
    /// </summary>
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
        options.Validate();

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.TwoStageOrderedCommit,
          sources.Count,
          options.EffectiveMaxDegreeOfParallelism,
          options.WorkType);
        if (sources.Count == 0)
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
            return;
        }

        try
        {
            using var admissionLease = AcquireAdmissionAsync(
              options.CreateAdmissionRequest(sources.Count),
              cancellationToken,
              telemetry).GetAwaiter().GetResult();
            using var admissionScope = PushAdmissionLease(admissionLease);
            var activeCollections = new List<ActiveWorkItem<TCollected>>(options.EffectiveMaxDegreeOfParallelism);
            var activeSolves = new List<ActiveWorkItem<TResult>>(options.EffectiveMaxDegreeOfParallelism);
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
                       completedCollections.Count + completedResults.Count < options.EffectiveReorderAllowance &&
                       completedRecordCount < options.EffectiveMaxCompletedRecordCount &&
                       CanScheduleWithinWindow(nextOrderToSchedule, nextOrderToCommit, options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var order = nextOrderToSchedule;
                    nextOrderToSchedule++;
                    activeCollections.Add(new ActiveWorkItem<TCollected>(order, Task.Run(
                      () =>
                      {
                          cancellationToken.ThrowIfCancellationRequested();
                          telemetry.WorkItemStarted();
                          try
                          {
                              var collected = collect(sources[order], order);
                              return new CompletedWorkItem<TCollected>(
                                order,
                                collected,
                                Math.Max(0, collectedRetainedRecordCount?.Invoke(collected) ?? 1));
                          }
                          finally
                          {
                              telemetry.WorkItemCompleted();
                          }
                      },
                      cancellationToken)));
                }

                while (activeCollections.Count + activeSolves.Count < options.EffectiveMaxDegreeOfParallelism &&
                       completedCollections.Remove(nextOrderToPrepare, out var collectedWorkItem))
                {
                    completedRecordCount -= collectedWorkItem.RetainedRecordCount;
                    telemetry.CompletedBufferChanged(completedCollections.Count + completedResults.Count);
                    telemetry.RetainedRecordCountChanged(completedRecordCount);
                    var order = collectedWorkItem.Order;
                    var prepared = prepare(collectedWorkItem.Result, order);
                    nextOrderToPrepare++;
                    activeSolves.Add(new ActiveWorkItem<TResult>(order, Task.Run(
                      () =>
                      {
                          cancellationToken.ThrowIfCancellationRequested();
                          telemetry.WorkItemStarted();
                          try
                          {
                              var result = solve(prepared, order);
                              return new CompletedWorkItem<TResult>(
                                order,
                                result,
                                Math.Max(0, resultRetainedRecordCount?.Invoke(result) ?? 1));
                          }
                          finally
                          {
                              telemetry.WorkItemCompleted();
                          }
                      },
                      cancellationToken)));
                }

                while (completedResults.Remove(nextOrderToCommit, out var resultWorkItem))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    commit(resultWorkItem.Result, nextOrderToCommit);
                    completedRecordCount -= resultWorkItem.RetainedRecordCount;
                    telemetry.CompletedBufferChanged(completedCollections.Count + completedResults.Count);
                    telemetry.RetainedRecordCountChanged(completedRecordCount);
                    nextOrderToCommit++;
                }

                if (nextOrderToCommit == sources.Count)
                {
                    return;
                }

                var completedBufferCount = completedCollections.Count + completedResults.Count;
                var headSolveTasks = activeSolves
                  .Where(item => item.Order == nextOrderToCommit)
                  .Select(item => (Task)item.Task)
                  .ToArray();
                var activeTasks = completedBufferCount >= options.EffectiveReorderAllowance - 1
                  ? headSolveTasks.Length > 0
                    ? headSolveTasks
                    : activeCollections
                      .Where(item => item.Order == nextOrderToPrepare)
                      .Select(item => (Task)item.Task)
                      .ToArray()
                  : activeCollections
                    .Select(item => (Task)item.Task)
                    .Concat(activeSolves.Select(item => (Task)item.Task))
                    .ToArray();
                if (activeTasks.Length == 0)
                {
                    activeTasks = activeCollections
                      .Select(item => (Task)item.Task)
                      .Concat(activeSolves.Select(item => (Task)item.Task))
                      .ToArray();
                    if (activeTasks.Length == 0 &&
                        (nextOrderToSchedule < sources.Count || completedCollections.ContainsKey(nextOrderToPrepare)))
                    {
                        continue;
                    }

                    if (activeTasks.Length == 0)
                    {
                        throw new InvalidOperationException("The two-stage ordered work window stopped before all results were committed.");
                    }
                }

                var completedTask = Task.WhenAny(activeTasks).GetAwaiter().GetResult();
                var completedCollection = activeCollections.FirstOrDefault(item => ReferenceEquals(item.Task, completedTask));
                if (completedCollection is not null)
                {
                    activeCollections.Remove(completedCollection);
                    try
                    {
                        var collectedWorkItem = completedCollection.Task.GetAwaiter().GetResult();
                        completedCollections.Add(collectedWorkItem.Order, collectedWorkItem);
                        completedRecordCount += collectedWorkItem.RetainedRecordCount;
                        telemetry.CompletedBufferChanged(completedCollections.Count + completedResults.Count);
                        telemetry.RetainedRecordCountChanged(completedRecordCount);
                    }
                    catch
                    {
                        WaitForWorkers(activeCollections.Select(item => (Task)item.Task).Concat(activeSolves.Select(item => (Task)item.Task)));
                        throw;
                    }
                }
                else
                {
                    var completedSolve = activeSolves.Single(item => ReferenceEquals(item.Task, completedTask));
                    activeSolves.Remove(completedSolve);
                    try
                    {
                        var resultWorkItem = completedSolve.Task.GetAwaiter().GetResult();
                        completedResults.Add(resultWorkItem.Order, resultWorkItem);
                        completedRecordCount += resultWorkItem.RetainedRecordCount;
                        telemetry.CompletedBufferChanged(completedCollections.Count + completedResults.Count);
                        telemetry.RetainedRecordCountChanged(completedRecordCount);
                    }
                    catch
                    {
                        WaitForWorkers(activeCollections.Select(item => (Task)item.Task).Concat(activeSolves.Select(item => (Task)item.Task)));
                        throw;
                    }
                }
            }
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 对输入源执行受限并发的异步遍历，并在首个失败后取消剩余工作。
    /// </summary>
    public async Task ForEachAsync<TSource>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(workItem);

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.ForEach,
          sources.Count,
          maxDegreeOfParallelism);

        if (sources.Count == 0)
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
            return;
        }

        try
        {
            using var admissionLease = await AcquireAdmissionAsync(
              new ConcurrencyAdmissionRequest(ConcurrencyWorkType.Throughput),
              cancellationToken,
              telemetry).ConfigureAwait(false);
            using var admissionScope = PushAdmissionLease(admissionLease);
            using var workItemCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                      int index;
                      lock (workItemStartGate)
                      {
                          if (workItemFailure is not null)
                          {
                              return;
                          }

                          cancellationToken.ThrowIfCancellationRequested();
                          index = Interlocked.Increment(ref nextIndex);
                          if (index >= sources.Count)
                          {
                              return;
                          }
                      }

                      try
                      {
                          workItemCancellation.Token.ThrowIfCancellationRequested();
                          telemetry.WorkItemStarted();
                          try
                          {
                              await workItem(sources[index], index, workItemCancellation.Token)
                                .ConfigureAwait(false);
                          }
                          finally
                          {
                              telemetry.WorkItemCompleted();
                          }
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
              CancellationToken.None);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            Volatile.Read(ref workItemFailure)?.Throw();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            telemetry.MarkCanceled();
            throw;
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// 按依赖关系调度工作项，只在依赖满足后才启动对应节点。
    /// </summary>
    public async Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(
        IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems,
        int maxDegreeOfParallelism,
        IComparer<TNode> readyOrder,
        CancellationToken cancellationToken = default)
        where TNode : notnull
    {
        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentNullException.ThrowIfNull(readyOrder);

        var telemetry = CreateTelemetry(
          ConcurrencyOperationKind.DependencyGraph,
          workItems.Count,
          maxDegreeOfParallelism,
          ConcurrencyWorkType.LatencySensitive);
        var failureDrainElapsed = TimeSpan.Zero;
        try
        {
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var admissionLease = await AcquireAdmissionAsync(
              new ConcurrencyAdmissionRequest(ConcurrencyWorkType.LatencySensitive),
              cancellationToken,
              telemetry).ConfigureAwait(false);
            using var admissionScope = PushAdmissionLease(admissionLease);

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
            telemetry.ReadyQueueChanged(peakReadyWorkItemCount);

            while (ready.Count > 0 || running.Count > 0)
            {
                runCancellation.Token.ThrowIfCancellationRequested();
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
                        await workItem.ExecuteAsync(dependencyResults, runCancellation.Token)),
                      CancellationToken.None);
                    running.Add(task, node);
                    telemetry.WorkItemStarted();
                    peakConcurrentWorkItemCount = Math.Max(peakConcurrentWorkItemCount, running.Count);
                }

                var completedTask = await Task.WhenAny(running.Keys);
                var completedNode = running[completedTask];
                running.Remove(completedTask);
                telemetry.WorkItemCompleted();
                DependencyCompletion<TNode, TResult> completed;
                try
                {
                    completed = await completedTask;
                }
                catch
                {
                    runCancellation.Cancel();
                    telemetry.MarkCanceled();
                    var failureDrainStopwatch = Stopwatch.StartNew();
                    try
                    {
                        await Task.WhenAll(running.Keys);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        failureDrainStopwatch.Stop();
                        failureDrainElapsed = failureDrainStopwatch.Elapsed;
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
                        telemetry.ReadyQueueChanged(ready.Count);
                    }
                }
            }

            if (results.Count != workItems.Count)
            {
                var incompleteNodes = workItemsByNode.Keys
                  .Where(node => !results.ContainsKey(node))
                  .OrderBy(node => node, readyOrder)
                  .Select(node => node.ToString())
                  .ToArray();
                throw new InvalidOperationException(
                  $"Dependency work graph contains a cycle involving: {string.Join(", ", incompleteNodes)}.");
            }

            return new DependencyExecutionResult<TNode, TResult>(
              results,
              peakReadyWorkItemCount,
              peakConcurrentWorkItemCount);
        }
        catch (OperationCanceledException)
        {
            telemetry.MarkCanceled();
            throw;
        }
        finally
        {
            telemetry.Report(cancellationToken.IsCancellationRequested, failureDrainElapsed);
        }
    }

    private sealed record CompletedWorkItem<TResult>(int Order, TResult Result, int RetainedRecordCount);

    private sealed record ActiveWorkItem<TResult>(int Order, Task<CompletedWorkItem<TResult>> Task);

    private sealed record DependencyCompletion<TNode, TResult>(TNode Node, TResult Result)
        where TNode : notnull;

    private static bool CanScheduleWithinWindow(
        int nextOrderToSchedule,
        int nextOrderToCommit,
        ConcurrencyWindowOptions options)
    {
        var maximumOutstandingWorkItemCount = Math.Min(
          (long)options.EffectiveReorderAllowance + 1,
          (long)options.EffectiveMaxCompletedRecordCount + 1);
        return (long)nextOrderToSchedule - nextOrderToCommit < maximumOutstandingWorkItemCount;
    }

    /// <summary>
    /// 为一次并发操作创建遥测跟踪器。
    /// </summary>
    private OperationTelemetryTracker CreateTelemetry(
        ConcurrencyOperationKind operationKind,
        int sourceCount,
        int requestedMaxDegreeOfParallelism,
        ConcurrencyWorkType workType = ConcurrencyWorkType.Throughput)
    {
        return new OperationTelemetryTracker(
          _telemetrySink,
          operationKind,
          sourceCount,
          requestedMaxDegreeOfParallelism,
          workType);
    }

    /// <summary>
    /// 在当前上下文尚未持有租约时申请新的并发准入租约。
    /// </summary>
    private async Task<ConcurrencyAdmissionController.ConcurrencyAdmissionLease?> AcquireAdmissionAsync(
        ConcurrencyAdmissionRequest request,
        CancellationToken cancellationToken,
        OperationTelemetryTracker telemetry)
    {
        if (_admissionController is null)
        {
            return null;
        }

        if (_currentAdmissionLease.Value is not null)
        {
            return null;
        }

        var lease = await _admissionController.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
        telemetry.AdmissionGranted(lease);
        return lease;
    }

    /// <summary>
    /// 将准入租约压入当前异步上下文，并返回可恢复旧值的作用域对象。
    /// </summary>
    private IDisposable? PushAdmissionLease(ConcurrencyAdmissionController.ConcurrencyAdmissionLease? lease)
    {
        if (lease is null)
        {
            return null;
        }

        var previous = _currentAdmissionLease.Value;
        _currentAdmissionLease.Value = lease;
        return new AdmissionLeaseScope(_currentAdmissionLease, previous);
    }

    private sealed class OperationTelemetryTracker
    {
        private readonly IConcurrencyPoolTelemetrySink? _telemetrySink;
        private readonly ConcurrencyOperationKind _operationKind;
        private readonly int _sourceCount;
        private readonly int _requestedMaxDegreeOfParallelism;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _activeWorkItemCount;
        private int _peakActiveWorkItemCount;
        private int _peakReadyWorkItemCount;
        private int _peakCompletedBufferItemCount;
        private int _peakRetainedRecordCount;
        private long _peakReservedByteCount;
        private long _queueWaitTicks;
        private int _workType;
        private int _admissionReason = -1;
        private int _wasCanceled;

        /// <summary>
        /// 初始化一次并发操作的遥测采样状态。
        /// </summary>
        public OperationTelemetryTracker(
            IConcurrencyPoolTelemetrySink? telemetrySink,
            ConcurrencyOperationKind operationKind,
            int sourceCount,
            int requestedMaxDegreeOfParallelism,
            ConcurrencyWorkType workType)
        {
            _telemetrySink = telemetrySink;
            _operationKind = operationKind;
            _sourceCount = sourceCount;
            _requestedMaxDegreeOfParallelism = requestedMaxDegreeOfParallelism;
            _workType = (int)workType;
        }

        /// <summary>
        /// 记录一个工作项开始执行。
        /// </summary>
        public void WorkItemStarted()
        {
            var activeWorkItemCount = Interlocked.Increment(ref _activeWorkItemCount);
            UpdateMaximum(ref _peakActiveWorkItemCount, activeWorkItemCount);
        }

        /// <summary>
        /// 记录一个工作项执行完成。
        /// </summary>
        public void WorkItemCompleted()
        {
            Interlocked.Decrement(ref _activeWorkItemCount);
        }

        /// <summary>
        /// 刷新就绪队列峰值。
        /// </summary>
        public void ReadyQueueChanged(int readyWorkItemCount)
        {
            UpdateMaximum(ref _peakReadyWorkItemCount, readyWorkItemCount);
        }

        /// <summary>
        /// 刷新已完成缓冲区峰值。
        /// </summary>
        public void CompletedBufferChanged(int completedBufferItemCount)
        {
            UpdateMaximum(ref _peakCompletedBufferItemCount, completedBufferItemCount);
        }

        /// <summary>
        /// 刷新保留记录数峰值。
        /// </summary>
        public void RetainedRecordCountChanged(int retainedRecordCount)
        {
            UpdateMaximum(ref _peakRetainedRecordCount, retainedRecordCount);
        }

        /// <summary>
        /// 标记本次操作已进入取消路径。
        /// </summary>
        public void MarkCanceled()
        {
            Volatile.Write(ref _wasCanceled, 1);
        }

        /// <summary>
        /// 记录准入控制返回的工作类别和排队信息。
        /// </summary>
        public void AdmissionGranted(ConcurrencyAdmissionController.ConcurrencyAdmissionLease lease)
        {
            Volatile.Write(ref _workType, (int)lease.Request.WorkType);
            Volatile.Write(ref _peakReservedByteCount, lease.Request.ReservedByteCount);
            Volatile.Write(ref _queueWaitTicks, lease.QueueWait.Ticks);
            Volatile.Write(ref _admissionReason, (int)lease.AdmissionReason);
        }

        /// <summary>
        /// 汇总本次操作的遥测数据并发送给接收器。
        /// </summary>
        public void Report(bool callerCancellationRequested, TimeSpan failureDrainElapsed)
        {
            _stopwatch.Stop();
            var telemetry = new ConcurrencyOperationTelemetry(
              _operationKind,
              _sourceCount,
              _requestedMaxDegreeOfParallelism,
              Volatile.Read(ref _peakActiveWorkItemCount),
              Volatile.Read(ref _peakReadyWorkItemCount),
              Volatile.Read(ref _peakCompletedBufferItemCount),
              Volatile.Read(ref _peakRetainedRecordCount),
              (ConcurrencyWorkType)Volatile.Read(ref _workType),
              Volatile.Read(ref _peakReservedByteCount),
              TimeSpan.FromTicks(Volatile.Read(ref _queueWaitTicks)),
              Volatile.Read(ref _admissionReason) is var admissionReason && admissionReason >= 0
                ? (ConcurrencyAdmissionReason)admissionReason
                : null,
              _stopwatch.Elapsed,
              callerCancellationRequested || Volatile.Read(ref _wasCanceled) != 0,
              failureDrainElapsed);
            try
            {
                _telemetrySink?.Record(telemetry);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 以无锁方式更新峰值计数。
        /// </summary>
        private static void UpdateMaximum(ref int target, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (candidate <= current ||
                    Interlocked.CompareExchange(ref target, candidate, current) == current)
                {
                    return;
                }
            }
        }

    }

    private sealed class AdmissionLeaseScope : IDisposable
    {
        private readonly AsyncLocal<ConcurrencyAdmissionController.ConcurrencyAdmissionLease?> _lease;
        private readonly ConcurrencyAdmissionController.ConcurrencyAdmissionLease? _previous;
        private int _disposed;

        /// <summary>
        /// 创建一个在释放时恢复先前租约的作用域对象。
        /// </summary>
        public AdmissionLeaseScope(
            AsyncLocal<ConcurrencyAdmissionController.ConcurrencyAdmissionLease?> lease,
            ConcurrencyAdmissionController.ConcurrencyAdmissionLease? previous)
        {
            _lease = lease;
            _previous = previous;
        }

        /// <summary>
        /// 恢复进入作用域前的租约状态。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _lease.Value = _previous;
            }
        }
    }

    /// <summary>
    /// 在异常路径上等待所有工作线程结束，并吞掉后续异常。
    /// </summary>
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
