using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace NL.Concurrency;

/// <summary>
/// 全 run 唯一的调度内核。拥有固定数量的长期 worker，是所有工作的唯一入口：
/// 有序选择、遍历、依赖图与长任务都是同一次提交的参数差异。
/// </summary>
/// <remarks>
/// 提交深度契约：本内核不实现可重入领取。调用方必须把整条流水线表达为
/// 单次提交内的扁平依赖图，而不是在工作项内部再次调用 <see cref="RunAsync{TResult}"/>。
/// 当全部 worker 都被等待内层提交的工作项占满时，内层提交无法推进。
/// </remarks>
public sealed class WorkScheduler : IAsyncDisposable
{
    /// <summary>
    /// 当前正在执行本内核工作项的异步流。用于把「嵌套提交」这一必然死锁的用法
    /// 变成可诊断的立即失败，而不是偶发的挂起。
    /// </summary>
    private static readonly AsyncLocal<WorkScheduler?> CurrentScheduler = new();

    private readonly WorkSchedulerOptions _options;
    private readonly IWorkTelemetrySink? _telemetry;
    private readonly object _stateGate = new();
    private readonly List<Submission> _submissions = new();
    private readonly List<Task> _workers = new();
    private readonly WorkerAccounting[] _workerAccounting = new WorkerAccounting[WorkSchedulerOptions.MaximumWorkerCount];
    private TaskCompletionSource _pulse = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private volatile bool _disposeRequested;
    private long _submissionSequence;

    /// <summary>创建内核。worker 在首次提交时惰性启动并常驻。</summary>
    /// <param name="options">内核配额。</param>
    /// <param name="telemetry">遥测接收端；为 null 时不产生遥测。</param>
    public WorkScheduler(WorkSchedulerOptions options, IWorkTelemetrySink? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _telemetry = telemetry;
    }

    /// <summary>长期 worker 数量，等于全部类别上限的最大值。</summary>
    public int WorkerCount => _options.WorkerCount;

    /// <summary>
    /// 本内核实际使用的配额。调用方需要报告「生效值」时必须读这里，
    /// 而不是把外部配置再映射一遍——注入内核时两者可能不同。
    /// </summary>
    public WorkSchedulerOptions Options => _options;

    /// <summary>
    /// 唯一入口。返回结果按 <see cref="WorkItem{TResult}.StableOrder"/> 升序归并；
    /// <see cref="WorkSubmission{TResult}.PreserveOrder"/> 为 false 时返回空列表。
    /// </summary>
    public async Task<IReadOnlyList<TResult>> RunAsync<TResult>(
        WorkSubmission<TResult> submission,
        CancellationToken cancellationToken = default)
    {
        var outcome = await RunWithMetricsAsync(submission, cancellationToken).ConfigureAwait(false);
        return outcome.Results;
    }

    /// <summary>
    /// 与 <see cref="RunAsync{TResult}"/> 相同，但额外返回本次提交观测到的调度峰值。
    /// 供需要上报 peak ready / peak concurrent 的调用方（规则图）使用。
    /// </summary>
    public async Task<WorkExecutionOutcome<TResult>> RunWithMetricsAsync<TResult>(
        WorkSubmission<TResult> submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        // 嵌套提交会把全部 worker 占满在等待上，必然死锁；立即给出可诊断的失败。
        // 这里必须识别**任意**已经处在工作项内的情形，而不只是同一个实例：
        // 计划禁止「在 worker 内新建第二个 scheduler」来绕开同实例守卫——那样会有
        // 两份独立的 P 同时存在，「统一 P」不再成立，且外层 worker 可能被同步等待占满。
        var activeScheduler = CurrentScheduler.Value;
        if (activeScheduler is not null)
        {
            throw new InvalidOperationException(
              ReferenceEquals(activeScheduler, this)
                ? "WorkScheduler.RunAsync was called from inside a work item of the same scheduler. " +
                  "Submissions must be flat: express dependent stages as items of a single submission " +
                  "with WorkItem.Dependencies instead of nesting RunAsync calls."
                : "WorkScheduler.RunAsync was called from inside a work item of a DIFFERENT scheduler. " +
                  "Submissions must be flat: a work item must not create or drive a second scheduler, " +
                  "wrap work in Task.Run, or block on it, because that would put two independent " +
                  "worker pools (P) in flight at once. Submit the work on the run owner instead.");
        }

        if (submission.Items.Count == 0)
        {
            return new WorkExecutionOutcome<TResult>(
              Array.Empty<TResult>(), 0, 0, TimeSpan.Zero);
        }

        var state = Submission<TResult>.Create(submission, _options, ++_submissionSequence, cancellationToken);

        lock (_stateGate)
        {
            // 与 DisposeAsync 的标记在同一次锁内判定，避免「先加入提交、worker 已全部退出」的挂起。
            if (_disposeRequested)
            {
                state.Cancellation.Dispose();
                throw new ObjectDisposedException(nameof(WorkScheduler));
            }

            EnsureWorkersStarted();
            _submissions.Add(state);
            state.SeedReady();
        }

        Pulse();

        await state.Completion.Task.ConfigureAwait(false);

        lock (_stateGate)
        {
            _submissions.Remove(state);
        }

        // 必须唤醒待命 worker：它们只有在空闲且无待处理提交时才会退出，
        // 而「移除已完成提交」正是让该条件成立的事件。
        Pulse();

        // 遥测是诊断信息，写入失败不得改变调度、成功或取消语义。
        EmitTelemetry(state);

        try
        {
            state.Failure?.Throw();
        }
        finally
        {
            state.Cancellation.Dispose();
        }

        return new WorkExecutionOutcome<TResult>(
          state.CollectResults(),
          state.PeakReadyCount,
          state.PeakActiveCount,
          TimeSpan.FromSeconds(Math.Max(
            0,
            (Stopwatch.GetTimestamp() - state.StartedTimestamp) / (double)Stopwatch.Frequency)));
    }

    private void EmitTelemetry(Submission state)
    {
        if (_telemetry is null)
        {
            return;
        }

        try
        {
            var elapsed = (Stopwatch.GetTimestamp() - state.StartedTimestamp) / (double)Stopwatch.Frequency;
            _telemetry.Record(new WorkTelemetry(
              state.Category,
              state.ItemCount,
              state.MaxConcurrency,
              state.PeakActiveCount,
              state.PeakReadyCount,
              state.PeakInFlightBytes,
              TimeSpan.FromSeconds(state.MaxQueueWaitSeconds),
              TimeSpan.FromSeconds(Math.Max(0, elapsed)),
              state.Failure is not null || state.Cancellation.IsCancellationRequested));
        }
        catch
        {
            // 与 RuntimeMeasurementLog.Emit 的 fail-open 行为一致。
        }
    }

    /// <summary>停止领取新工作，等待在途提交结束，然后结束全部 worker。</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
        }

        Pulse();

        await Task.WhenAll(_workers).ConfigureAwait(false);
    }

    private void EnsureWorkersStarted()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        for (var index = 0; index < _options.WorkerCount; index++)
        {
            var accounting = new WorkerAccounting();
            _workerAccounting[index] = accounting;
            _workers.Add(Task.Run(() => WorkerLoopAsync(accounting)));
        }
    }

    /// <summary>
    /// 每个长期 worker 的忙碌/空闲记账快照，下标即 worker 编号。
    /// </summary>
    /// <remarks>
    /// 未启动（尚无提交）或下标超出实际 worker 数时该项为 null。
    /// 记账在 worker 热路径上只做两次时间戳读取，不引入锁。
    /// </remarks>
    public IReadOnlyList<WorkerUtilization?> GetWorkerUtilization()
    {
        var count = _options.WorkerCount;
        var snapshot = new WorkerUtilization?[count];
        for (var index = 0; index < count; index++)
        {
            snapshot[index] = _workerAccounting[index]?.Snapshot(index);
        }

        return snapshot;
    }

    private async Task WorkerLoopAsync(WorkerAccounting accounting)
    {
        accounting.Start();
        while (true)
        {
            // 先捕获信号门再尝试领取：这样任何后续的状态变更都会唤醒本次等待，
            // 不会丢失唤醒。
            var gate = Volatile.Read(ref _pulse);
            var ticket = TakeReady();

            if (ticket is not null)
            {
                Exception? error = null;
                var busyStarted = Stopwatch.GetTimestamp();
                try
                {
                    var previous = CurrentScheduler.Value;
                    CurrentScheduler.Value = this;
                    try
                    {
                        await ticket.Invoke(ticket.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        CurrentScheduler.Value = previous;
                    }
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    // 只把「执行工作项」计入忙碌；等待唤醒、领取与结算都算空闲。
                    accounting.RecordItem(Stopwatch.GetTimestamp() - busyStarted);
                }

                FinishItem(ticket.Submission, ticket.Index, error);
                continue;
            }

            // 释放时只停止领取新提交，仍要把在途提交服务完，
            // 否则已 await RunAsync 的调用方会永久挂起。
            if (_disposeRequested && !HasPendingSubmissions())
            {
                return;
            }

            await gate.Task.ConfigureAwait(false);
        }
    }

    private bool HasPendingSubmissions()
    {
        lock (_stateGate)
        {
            return _submissions.Count > 0;
        }
    }

    private Ticket? TakeReady()
    {
        lock (_stateGate)
        {
            Submission? bestSubmission = null;
            Submission.Candidate? bestCandidate = null;
            var now = Stopwatch.GetTimestamp();
            var ageThreshold = _options.MaximumThroughputWait.TotalSeconds;

            // 就绪序由每个提交自身的两个优先队列给出（见 Submission.PeekBest），
            // 故此处只按「提交数」线性扫描；提交数由并发的提交方数量决定，
            // 与单个提交的工作项数 N 无关。派发 N 项的代价因此是 O(N log N)。
            foreach (var submission in _submissions)
            {
                if (submission.Failure is not null || submission.ActiveCount >= submission.MaxConcurrency)
                {
                    continue;
                }

                var candidate = submission.PeekBest(now, ageThreshold);
                if (candidate is null)
                {
                    continue;
                }

                if (bestCandidate is null)
                {
                    bestSubmission = submission;
                    bestCandidate = candidate;
                    continue;
                }

                // 与原先的全表扫描使用完全相同的比较键：
                // 优先级升序 → 成本降序 → StableOrder 升序 → 提交序号升序。
                var incumbent = bestCandidate.Value;
                var challenger = candidate.Value;
                var better = challenger.PriorityClass < incumbent.PriorityClass
                  || (challenger.PriorityClass == incumbent.PriorityClass && challenger.Cost > incumbent.Cost)
                  || (challenger.PriorityClass == incumbent.PriorityClass && challenger.Cost == incumbent.Cost
                    && challenger.StableOrder < incumbent.StableOrder)
                  || (challenger.PriorityClass == incumbent.PriorityClass && challenger.Cost == incumbent.Cost
                    && challenger.StableOrder == incumbent.StableOrder
                    && submission.Sequence < bestSubmission!.Sequence);

                if (better)
                {
                    bestSubmission = submission;
                    bestCandidate = candidate;
                }
            }

            if (bestSubmission is null || bestCandidate is null)
            {
                return null;
            }

            var bestIndex = bestSubmission.Take(bestCandidate.Value);
            var enqueuedAt = bestSubmission.ReadySince(bestIndex);
            bestSubmission.ActiveCount++;
            bestSubmission.InFlightBytes += bestSubmission.Bytes[bestIndex];
            if (bestSubmission.ActiveCount > bestSubmission.PeakActiveCount)
            {
                bestSubmission.PeakActiveCount = bestSubmission.ActiveCount;
            }

            if (bestSubmission.InFlightBytes > bestSubmission.PeakInFlightBytes)
            {
                bestSubmission.PeakInFlightBytes = bestSubmission.InFlightBytes;
            }

            var waited = (now - enqueuedAt) / (double)Stopwatch.Frequency;
            if (waited > bestSubmission.MaxQueueWaitSeconds)
            {
                bestSubmission.MaxQueueWaitSeconds = waited;
            }

            bestSubmission.ObserveReadyDepth();

            return new Ticket(bestSubmission, bestIndex, bestSubmission.CreateInvocation(bestIndex));
        }
    }

    private static bool IsBytesAllowed(Submission submission, int index)
    {
        if (submission.MaxInFlightBytes <= 0)
        {
            return true;
        }

        var bytes = submission.Bytes[index];

        // 估算为 0 的项永不阻塞，否则未接线估算的调用方会整体卡死。
        if (bytes == 0)
        {
            return true;
        }

        // 用减法而非 `InFlightBytes + bytes <= MaxInFlightBytes`：后者在 InFlightBytes
        // 接近 long.MaxValue 时会回绕成负数，从而**错误放行**超出额度的项。
        // MaxInFlightBytes 已在上方确认 > 0 且 bytes >= 0，故 `Max - bytes` 不会溢出。
        if (submission.InFlightBytes <= submission.MaxInFlightBytes - bytes)
        {
            return true;
        }

        // 单项超过总额度时放行一次，否则估算失真的巨项会永久饿死。
        return bytes > submission.MaxInFlightBytes && submission.InFlightBytes == 0;
    }

    private void FinishItem(Submission submission, int index, Exception? error)
    {
        TaskCompletionSource? completion = null;

        lock (_stateGate)
        {
            submission.ActiveCount--;
            submission.InFlightBytes -= submission.Bytes[index];

            if (error is null)
            {
                submission.CompletedCount++;
                foreach (var dependent in submission.Dependents[index])
                {
                    if (--submission.Remaining[dependent] == 0)
                    {
                        submission.EnqueueReady(dependent, Stopwatch.GetTimestamp());
                    }
                }

                submission.ObserveReadyDepth();
            }
            else
            {
                submission.Failure ??= ExceptionDispatchInfo.Capture(error);
                submission.Cancel();
            }

            if (submission.ActiveCount == 0 &&
              (submission.CompletedCount == submission.ItemCount || submission.Failure is not null))
            {
                completion = submission.Completion;
            }
        }

        Pulse();
        completion?.TrySetResult();
    }

    private void Pulse()
    {
        var previous = Interlocked.Exchange(
          ref _pulse,
          new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    /// <summary>
    /// 单个 worker 的忙碌/空闲记账。由该 worker 的循环独占写入，
    /// 读取侧可能并发，故计数字段用 <see cref="Interlocked"/> 维护。
    /// </summary>
    private sealed class WorkerAccounting
    {
        private long _busyTimestampTicks;
        private long _startedTimestampTicks;
        private long _executedItemCount;

        public void Start()
        {
            Interlocked.Exchange(ref _startedTimestampTicks, Stopwatch.GetTimestamp());
        }

        public void RecordItem(long busyTicks)
        {
            Interlocked.Add(ref _busyTimestampTicks, busyTicks);
            Interlocked.Increment(ref _executedItemCount);
        }

        public WorkerUtilization? Snapshot(int workerIndex)
        {
            var startedTicks = Interlocked.Read(ref _startedTimestampTicks);
            if (startedTicks == 0)
            {
                return null;
            }

            var nowTicks = Stopwatch.GetTimestamp();
            var busyTicks = Interlocked.Read(ref _busyTimestampTicks);
            var lifetime = ToTimeSpan(nowTicks - startedTicks);
            var busy = ToTimeSpan(Math.Min(busyTicks, nowTicks - startedTicks));
            return new WorkerUtilization(
              workerIndex,
              Interlocked.Read(ref _executedItemCount),
              busy,
              lifetime - busy,
              lifetime);
        }

        private static TimeSpan ToTimeSpan(long ticks) =>
          TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
    }

    private sealed class Ticket(
      Submission submission,
      int index,
      Func<CancellationToken, Task> invoke)
    {
        public Submission Submission { get; } = submission;

        public int Index { get; } = index;

        public Func<CancellationToken, Task> Invoke { get; } = invoke;

        public CancellationToken Token => Submission.Cancellation.Token;
    }

    private abstract class Submission
    {
        protected Submission(
          int itemCount,
          long[] stableOrders,
          int[] costs,
          long[] bytes,
          WorkPriority[] priorities,
          List<int>[] dependents,
          int[] remaining,
          string category,
          long maxInFlightBytes,
          int maxConcurrency,
          bool preserveOrder,
          long sequence,
          CancellationTokenSource cancellation)
        {
            ItemCount = itemCount;
            StableOrders = stableOrders;
            Costs = costs;
            Bytes = bytes;
            Priorities = priorities;
            Dependents = dependents;
            Remaining = remaining;
            Category = category;
            MaxInFlightBytes = maxInFlightBytes;
            MaxConcurrency = maxConcurrency;
            PreserveOrder = preserveOrder;
            Sequence = sequence;
            Cancellation = cancellation;
            _since = new long[itemCount];
            _readyFlags = new bool[itemCount];
            _promoted = new bool[itemCount];
        }

        public int ItemCount { get; }

        public long[] StableOrders { get; }

        public int[] Costs { get; }

        public long[] Bytes { get; }

        /// <summary>项的优先级类别；这里保留原始声明值，老化提升另由 <c>_promoted</c> 记录。</summary>
        public WorkPriority[] Priorities { get; }

        public List<int>[] Dependents { get; }

        public int[] Remaining { get; }

        public string Category { get; }

        public long MaxInFlightBytes { get; }

        public int MaxConcurrency { get; }

        public bool PreserveOrder { get; }

        public long Sequence { get; }

        public CancellationTokenSource Cancellation { get; }

        public TaskCompletionSource Completion { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 就绪项的入队时间戳，按项下标索引。取代原先的
        /// <c>List&lt;(int Index, long Since)&gt;</c>：它同时消除了摘取时的
        /// <c>List.RemoveAt</c>（O(n) 元素搬移）。
        /// </summary>
        private readonly long[] _since;

        /// <summary>
        /// 项当前是否仍在就绪集中（入队置真，领取置假）。用显式标志而不是
        /// 「<c>_since != 0</c>」这类哨兵：后者依赖 <c>Stopwatch.GetTimestamp()</c>
        /// 永不为 0，是未文档化的假设。
        /// </summary>
        private readonly bool[] _readyFlags;

        /// <summary>已由老化提升从吞吐队列移入紧急队列的项。</summary>
        private readonly bool[] _promoted;

        /// <summary>
        /// 按入队时间戳升序排列的「仍是吞吐类」项。老化提升只做单向提升
        /// （吞吐 → 紧急），故只需检查队首：队首未老化则其后各项必然也未老化。
        /// </summary>
        private readonly Queue<int> _agingOrder = new();

        /// <summary>
        /// 优先级 0（延迟敏感，以及已老化的吞吐项）：成本降序 → StableOrder 升序。
        /// 键取 <c>(-cost, stableOrder)</c>，因 .NET 的 PriorityQueue 是最小堆，
        /// 而原始语义是成本**降序**；StableOrder 在单次提交内唯一（见 Create 的重复校验），
        /// 故键全序，不依赖堆的稳定性。
        /// </summary>
        private readonly PriorityQueue<int, (int CostDesc, long StableOrder)> _urgent = new();

        /// <summary>优先级 1（尚未老化的吞吐项），排序键同 <see cref="_urgent"/>。</summary>
        private readonly PriorityQueue<int, (int CostDesc, long StableOrder)> _deferred = new();

        /// <summary>
        /// 当前就绪项数。不能读两个队列的 Count：惰性删除会留下失效条目。
        /// 这是 <see cref="PeakReadyCount"/> 的输入，必须与旧 <c>Ready.Count</c> 等价。
        /// </summary>
        private int _readyCount;

        /// <summary>就绪序的一个候选：比较键与所属优先级类别。</summary>
        public readonly record struct Candidate(int Index, int PriorityClass, int Cost, long StableOrder);

        /// <summary>
        /// 本次提交当前最优的就绪项，无就绪项时为 null。
        /// <para>
        /// 先做老化提升（单调、均摊 O(log n)），再取两类各自的最优；
        /// 优先级 0 恒优于优先级 1，与旧全表扫描的 <c>priority &lt; bestPriority</c> 一致。
        /// </para>
        /// <para>
        /// 字节预算会否决队首时（<see cref="IsBytesAllowed"/> 为假），退回一次线性扫描，
        /// 以保持「跳过不合预算项、取预算内最优」的原有语义——预算过滤依赖运行期的
        /// <c>InFlightBytes</c>，无法编入静态堆键。该路径在生产中不触发：
        /// <c>MaxInFlightBytes</c> 为 0（不限）时所有项恒被允许。
        /// </para>
        /// </summary>
        public Candidate? PeekBest(long now, double ageThreshold)
        {
            PromoteAged(now, ageThreshold);

            var urgent = PeekLive(_urgent, priorityClass: 0, index => !_readyFlags[index]);
            var deferred = PeekLive(_deferred, priorityClass: 1, index => !_readyFlags[index] || _promoted[index]);

            // 优先级 0 恒优于优先级 1；同类内堆顶即最优。
            var best = urgent ?? deferred;
            if (best is null)
            {
                return null;
            }

            if (IsBytesAllowed(this, best.Value.Index))
            {
                return best;
            }

            // 队首不合预算：预算内可能仍有更次优的项，必须全表扫描才能保持原语义。
            return ScanBestAllowed();
        }

        private static Candidate? PeekLive(
          PriorityQueue<int, (int CostDesc, long StableOrder)> queue,
          int priorityClass,
          Func<int, bool> isStale)
        {
            while (queue.Count > 0 && isStale(queue.Peek()))
            {
                queue.Dequeue();
            }

            if (queue.Count == 0)
            {
                return null;
            }

            queue.TryPeek(out var index, out var key);
            return new Candidate(index, priorityClass, -key.CostDesc, key.StableOrder);
        }

        /// <summary>把已超龄的吞吐项提升到紧急队列。提升单向，故只需推进队首。</summary>
        private void PromoteAged(long now, double ageThreshold)
        {
            while (_agingOrder.Count > 0)
            {
                var index = _agingOrder.Peek();
                var waited = (now - _since[index]) / (double)Stopwatch.Frequency;
                if (waited < ageThreshold)
                {
                    return;
                }

                _agingOrder.Dequeue();
                if (!_readyFlags[index] || _promoted[index])
                {
                    continue;
                }

                _promoted[index] = true;
                _urgent.Enqueue(index, (-Costs[index], StableOrders[index]));
            }
        }

        /// <summary>预算否决队首时的兜底：在全就绪集上按原比较键取预算内最优。</summary>
        private Candidate? ScanBestAllowed()
        {
            Candidate? best = null;
            for (var index = 0; index < ItemCount; index++)
            {
                if (!_readyFlags[index])
                {
                    continue;
                }

                if (!IsBytesAllowed(this, index))
                {
                    continue;
                }

                var isUrgentClass = Priorities[index] == WorkPriority.LatencySensitive || _promoted[index];
                var candidate = new Candidate(index, isUrgentClass ? 0 : 1, Costs[index], StableOrders[index]);
                if (best is null || IsBetter(candidate, best.Value))
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static bool IsBetter(Candidate challenger, Candidate incumbent)
        {
            if (challenger.PriorityClass != incumbent.PriorityClass)
            {
                return challenger.PriorityClass < incumbent.PriorityClass;
            }

            if (challenger.Cost != incumbent.Cost)
            {
                return challenger.Cost > incumbent.Cost;
            }

            return challenger.StableOrder < incumbent.StableOrder;
        }

        /// <summary>领取候选：清就绪标志并维护就绪计数；返回项下标。</summary>
        public int Take(Candidate candidate)
        {
            _readyFlags[candidate.Index] = false;
            _readyCount--;
            return candidate.Index;
        }

        /// <summary>
        /// 把一个项加入就绪集。取代原先的 <c>Ready.Add(...)</c>。
        /// 延迟敏感项直接进紧急队列；吞吐项先进吞吐队列并登记老化顺序
        /// （入队时间戳单调不减，故该队列天然按时间升序）。
        /// </summary>
        public void EnqueueReady(int index, long timestamp)
        {
            _since[index] = timestamp;
            _readyFlags[index] = true;
            _readyCount++;

            if (Priorities[index] == WorkPriority.LatencySensitive)
            {
                _urgent.Enqueue(index, (-Costs[index], StableOrders[index]));
                return;
            }

            _deferred.Enqueue(index, (-Costs[index], StableOrders[index]));
            _agingOrder.Enqueue(index);
        }

        public long ReadySince(int index) => _since[index];

        public int ActiveCount { get; set; }

        public long InFlightBytes { get; set; }

        public int CompletedCount { get; set; }

        public int PeakActiveCount { get; set; }

        public int PeakReadyCount { get; set; }

        public long PeakInFlightBytes { get; set; }

        /// <summary>提交被接受的时刻，用于结算总耗时。</summary>
        public long StartedTimestamp { get; set; }

        /// <summary>单项在就绪队列中的最长等待秒数。</summary>
        public double MaxQueueWaitSeconds { get; set; }

        public ExceptionDispatchInfo? Failure { get; set; }

        public abstract Func<CancellationToken, Task> CreateInvocation(int index);

        public void SeedReady()
        {
            var now = Stopwatch.GetTimestamp();
            StartedTimestamp = now;
            for (var index = 0; index < ItemCount; index++)
            {
                if (Remaining[index] == 0)
                {
                    EnqueueReady(index, now);
                }
            }

            ObserveReadyDepth();
        }

        public void ObserveReadyDepth()
        {
            if (_readyCount > PeakReadyCount)
            {
                PeakReadyCount = _readyCount;
            }
        }

        public void Cancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 提交已结束；无剩余工作需要取消。
            }
        }
    }

    private sealed class Submission<TResult> : Submission
    {
        private readonly WorkItem<TResult>[] _items;
        private readonly long[] _stableOrderLookup;
        private readonly int[][] _dependencyIndexes;
        private readonly TResult[] _results;
        private readonly bool _returnOrderedResults;

        private Submission(
          WorkItem<TResult>[] items,
          int[][] dependencyIndexes,
          long[] stableOrders,
          int[] costs,
          long[] bytes,
          WorkPriority[] priorities,
          List<int>[] dependents,
          int[] remaining,
          string category,
          long maxInFlightBytes,
          int maxConcurrency,
          bool preserveOrder,
          long sequence,
          CancellationTokenSource cancellation)
          : base(
            items.Length,
            stableOrders,
            costs,
            bytes,
            priorities,
            dependents,
            remaining,
            category,
            maxInFlightBytes,
            maxConcurrency,
            preserveOrder,
            sequence,
            cancellation)
        {
            _items = items;
            _stableOrderLookup = stableOrders;
            _dependencyIndexes = dependencyIndexes;
            // 结果数组始终分配：即使 PreserveOrder 为 false，有依赖的项也需要读取前置结果。
            _results = new TResult[items.Length];
            _returnOrderedResults = preserveOrder;
        }

        public static Submission<TResult> Create(
          WorkSubmission<TResult> submission,
          WorkSchedulerOptions options,
          long sequence,
          CancellationToken cancellationToken)
        {
            var items = submission.Items.ToArray();
            var itemCount = items.Length;
            var stableOrders = new long[itemCount];
            var indexByStableOrder = new Dictionary<long, int>(itemCount);

            for (var index = 0; index < itemCount; index++)
            {
                stableOrders[index] = items[index].StableOrder;
                if (!indexByStableOrder.TryAdd(stableOrders[index], index))
                {
                    throw new ArgumentException(
                      $"Duplicate StableOrder {stableOrders[index]} in one submission.",
                      nameof(submission));
                }
            }

            var costs = new int[itemCount];
            var bytes = new long[itemCount];
            var priorities = new WorkPriority[itemCount];
            var dependents = new List<int>[itemCount];
            var remaining = new int[itemCount];
            var dependencyIndexes = new int[itemCount][];

            for (var index = 0; index < itemCount; index++)
            {
                var item = items[index];
                if (item.EstimatedBytes < 0)
                {
                    // 负值会污染在途记账：归还时 `InFlightBytes -= 负数` 反而推高计数。
                    // 与重复 StableOrder、缺失依赖一致，在提交前诊断性拒绝。
                    throw new ArgumentOutOfRangeException(
                      nameof(submission),
                      item.EstimatedBytes,
                      $"Work item {item.StableOrder} has a negative EstimatedBytes.");
                }

                if (item.EstimatedCost < 0)
                {
                    throw new ArgumentOutOfRangeException(
                      nameof(submission),
                      item.EstimatedCost,
                      $"Work item {item.StableOrder} has a negative EstimatedCost.");
                }

                costs[index] = item.EstimatedCost;
                bytes[index] = item.EstimatedBytes;
                priorities[index] = item.Priority;
                dependents[index] = [];

                var declared = item.Dependencies ?? [];
                var resolved = new int[declared.Count];
                for (var position = 0; position < declared.Count; position++)
                {
                    if (!indexByStableOrder.TryGetValue(declared[position], out var dependencyIndex))
                    {
                        throw new ArgumentException(
                          $"Work item {stableOrders[index]} depends on missing StableOrder {declared[position]}.",
                          nameof(submission));
                    }

                    resolved[position] = dependencyIndex;
                }

                dependencyIndexes[index] = resolved.Distinct().ToArray();
                remaining[index] = dependencyIndexes[index].Length;
            }

            for (var index = 0; index < itemCount; index++)
            {
                foreach (var dependencyIndex in dependencyIndexes[index])
                {
                    dependents[dependencyIndex].Add(index);
                }
            }

            DetectCycle(stableOrders, remaining, dependents, submission);

            var limit = submission.MaxConcurrency ?? options.ResolveLimit(submission.Category);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            return new Submission<TResult>(
              items,
              dependencyIndexes,
              stableOrders,
              costs,
              bytes,
              priorities,
              dependents,
              remaining,
              submission.Category,
              submission.MaxInFlightBytes,
              Math.Max(1, limit),
              submission.PreserveOrder,
              sequence,
              cancellation);
        }

        private static void DetectCycle(
          long[] stableOrders,
          int[] remaining,
          List<int>[] dependents,
          WorkSubmission<TResult> submission)
        {
            var pending = (int[])remaining.Clone();
            var queue = new Queue<int>();

            for (var index = 0; index < pending.Length; index++)
            {
                if (pending[index] == 0)
                {
                    queue.Enqueue(index);
                }
            }

            var settled = 0;
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                settled++;
                foreach (var dependent in dependents[index])
                {
                    if (--pending[dependent] == 0)
                    {
                        queue.Enqueue(dependent);
                    }
                }
            }

            if (settled == pending.Length)
            {
                return;
            }

            var cyclic = Enumerable.Range(0, pending.Length)
              .Where(index => pending[index] > 0)
              .Select(index => stableOrders[index]);

            throw new InvalidOperationException(
              $"Dependency cycle detected among StableOrder values: {string.Join(", ", cyclic)}.");
        }

        public override Func<CancellationToken, Task> CreateInvocation(int index)
        {
            var dependencies = _dependencyIndexes[index];
            IReadOnlyDictionary<long, TResult> inputs;

            if (dependencies.Length == 0)
            {
                inputs = EmptyInputs.Instance;
            }
            else
            {
                var map = new Dictionary<long, TResult>(dependencies.Length);
                foreach (var dependencyIndex in dependencies)
                {
                    map[_stableOrderLookup[dependencyIndex]] = _results![dependencyIndex];
                }

                inputs = map;
            }

            var item = _items[index];
            var results = _results;
            return async token =>
            {
                results[index] = await item.ExecuteAsync(inputs, token).ConfigureAwait(false);
            };
        }

        public IReadOnlyList<TResult> CollectResults()
        {
            if (!_returnOrderedResults)
            {
                return Array.Empty<TResult>();
            }

            var order = Enumerable.Range(0, ItemCount)
              .OrderBy(index => StableOrders[index])
              .ToArray();

            var ordered = new TResult[order.Length];
            for (var position = 0; position < order.Length; position++)
            {
                ordered[position] = _results[order[position]];
            }

            return ordered;
        }

        private static class EmptyInputs
        {
            public static readonly IReadOnlyDictionary<long, TResult> Instance =
              new Dictionary<long, TResult>();
        }
    }
}
