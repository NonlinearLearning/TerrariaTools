using System.Diagnostics;

namespace NL.Concurrency;

/// <summary>
/// 在单个运行时内协调独立操作的准入。已准入操作内部的执行顺序不在这里调整。
/// </summary>
public sealed class ConcurrencyAdmissionController
{
    private readonly object _gate = new();
    private readonly ConcurrencyAdmissionOptions _options;
    private readonly Queue<Waiter> _latencySensitiveWaiters = new();
    private readonly Queue<Waiter> _throughputWaiters = new();
    private ConcurrencyWorkType _nextType = ConcurrencyWorkType.LatencySensitive;
    private int _remainingTypeCredit;
    private int _activeOperationCount;
    private int _reservedItemCount;
    private long _reservedByteCount;

    /// <summary>
    /// 初始化一个新的准入控制器，并验证准入策略参数。
    /// </summary>
    /// <param name="options">当前运行时使用的准入上限与权重配置。</param>
    public ConcurrencyAdmissionController(ConcurrencyAdmissionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _remainingTypeCredit = options.LatencySensitiveWeight;
    }

    /// <summary>
    /// 为一次独立操作申请准入租约。
    /// </summary>
    /// <param name="request">本次操作声明的工作类别与资源预留。</param>
    /// <param name="cancellationToken">等待准入期间使用的取消令牌。</param>
    /// <returns>表示当前操作已获准入的租约。</returns>
    public Task<ConcurrencyAdmissionLease> AcquireAsync(
        ConcurrencyAdmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate(_options);
        cancellationToken.ThrowIfCancellationRequested();

        Waiter? waiter = null;
        lock (_gate)
        {
            waiter = new Waiter(request);
            Enqueue(waiter);
            TryAdmitWaiters();
        }

        return WaitForLeaseAsync(waiter, cancellationToken);
    }

    /// <summary>
    /// 等待排队项被准入，并在等待期间响应取消。
    /// </summary>
    /// <param name="waiter">当前请求对应的排队项。</param>
    /// <param name="cancellationToken">等待期间使用的取消令牌。</param>
    /// <returns>排队项获准后生成的租约。</returns>
    private async Task<ConcurrencyAdmissionLease> WaitForLeaseAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
          static state => ((CancellationRegistrationState)state!).Cancel(),
          new CancellationRegistrationState(this, waiter));
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 按工作类别把排队项加入对应队列。
    /// </summary>
    /// <param name="waiter">要入队的排队项。</param>
    private void Enqueue(Waiter waiter)
    {
        GetQueue(waiter.Request.WorkType).Enqueue(waiter);
    }

    /// <summary>
    /// 在当前并发与资源配额内尽量推进队首排队项准入。
    /// </summary>
    private void TryAdmitWaiters()
    {
        while (_activeOperationCount < _options.MaxConcurrentOperations)
        {
            var waiter = SelectNextWaiter();
            if (waiter is null)
            {
                return;
            }

            if (!Fits(waiter.Request))
            {
                return;
            }

            var queue = GetQueue(waiter.Request.WorkType);
            if (!ReferenceEquals(queue.Peek(), waiter))
            {
                throw new InvalidOperationException("Admission queue order was corrupted.");
            }

            queue.Dequeue();
            _activeOperationCount++;
            _reservedItemCount += waiter.Request.ReservedItemCount;
            _reservedByteCount += waiter.Request.ReservedByteCount;
            ConsumeCredit(waiter.Request.WorkType);
            var lease = new ConcurrencyAdmissionLease(
              this,
              waiter.Request,
              waiter.AdmissionReason,
              waiter.Stopwatch.Elapsed);
            waiter.Completion.TrySetResult(lease);
        }
    }

    /// <summary>
    /// 选择下一个可以准入的排队项，优先处理老化升级或当前权重轮次命中的队首。
    /// </summary>
    /// <returns>可准入的排队项；没有则返回 <see langword="null"/>。</returns>
    private Waiter? SelectNextWaiter()
    {
        DiscardCanceledHeads(_latencySensitiveWaiters);
        DiscardCanceledHeads(_throughputWaiters);

        if (_throughputWaiters.TryPeek(out var throughputWaiter) &&
            throughputWaiter.Stopwatch.Elapsed >= _options.EffectiveMaximumThroughputWait &&
            Fits(throughputWaiter.Request))
        {
            throughputWaiter.AdmissionReason = ConcurrencyAdmissionReason.AgingPromotion;
            return throughputWaiter;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var queue = GetQueue(_nextType);
            if (queue.TryPeek(out var waiter) && Fits(waiter.Request))
            {
                waiter.AdmissionReason = ConcurrencyAdmissionReason.WeightedTurn;
                return waiter;
            }

            SwitchTypeWithoutSpendingCredit();
        }

        return null;
    }

    /// <summary>
    /// 消耗一次当前工作类别的轮转配额，并在配额用尽后切换到另一类别。
    /// </summary>
    /// <param name="admittedType">本次实际获准入的工作类别。</param>
    private void ConsumeCredit(ConcurrencyWorkType admittedType)
    {
        if (admittedType != _nextType)
        {
            _nextType = admittedType;
            _remainingTypeCredit = WeightFor(admittedType);
        }

        _remainingTypeCredit--;
        if (_remainingTypeCredit == 0)
        {
            _nextType = Other(_nextType);
            _remainingTypeCredit = WeightFor(_nextType);
        }
    }

    /// <summary>
    /// 在不消耗当前配额的前提下切换轮转类别。
    /// </summary>
    private void SwitchTypeWithoutSpendingCredit()
    {
        _nextType = Other(_nextType);
        _remainingTypeCredit = WeightFor(_nextType);
    }

    /// <summary>
    /// 判断当前请求是否仍能放入剩余资源配额。
    /// </summary>
    /// <param name="request">待检查的准入请求。</param>
    /// <returns>资源配额足够时返回 <see langword="true"/>。</returns>
    private bool Fits(ConcurrencyAdmissionRequest request)
    {
        return _reservedItemCount <= _options.MaxReservedItemCount - request.ReservedItemCount &&
          _reservedByteCount <= _options.MaxReservedByteCount - request.ReservedByteCount;
    }

    /// <summary>
    /// 根据工作类别返回对应的等待队列。
    /// </summary>
    /// <param name="workType">要查询的工作类别。</param>
    /// <returns>该类别使用的等待队列。</returns>
    private Queue<Waiter> GetQueue(ConcurrencyWorkType workType)
    {
        return workType == ConcurrencyWorkType.LatencySensitive
          ? _latencySensitiveWaiters
          : _throughputWaiters;
    }

    /// <summary>
    /// 返回指定工作类别的轮转权重。
    /// </summary>
    /// <param name="workType">要查询的工作类别。</param>
    /// <returns>该类别对应的轮转权重。</returns>
    private int WeightFor(ConcurrencyWorkType workType)
    {
        return workType == ConcurrencyWorkType.LatencySensitive
          ? _options.LatencySensitiveWeight
          : _options.ThroughputWeight;
    }

    /// <summary>
    /// 返回与当前类别相对的另一种工作类别。
    /// </summary>
    /// <param name="workType">当前工作类别。</param>
    /// <returns>另一种工作类别。</returns>
    private static ConcurrencyWorkType Other(ConcurrencyWorkType workType)
    {
        return workType == ConcurrencyWorkType.LatencySensitive
          ? ConcurrencyWorkType.Throughput
          : ConcurrencyWorkType.LatencySensitive;
    }

    /// <summary>
    /// 丢弃队首已取消的排队项，直到队首变为有效项或队列为空。
    /// </summary>
    /// <param name="queue">要清理的等待队列。</param>
    private static void DiscardCanceledHeads(Queue<Waiter> queue)
    {
        while (queue.TryPeek(out var waiter) && waiter.IsCanceled)
        {
            queue.Dequeue();
        }
    }

    /// <summary>
    /// 取消尚未获准入的排队项，并尝试推进后续等待项。
    /// </summary>
    /// <param name="waiter">要取消的排队项。</param>
    private void Cancel(Waiter waiter)
    {
        lock (_gate)
        {
            waiter.IsCanceled = true;
            waiter.Completion.TrySetCanceled();
            TryAdmitWaiters();
        }
    }

    /// <summary>
    /// 释放一个已准入租约占用的并发与资源配额。
    /// </summary>
    /// <param name="lease">要释放的准入租约。</param>
    private void Release(ConcurrencyAdmissionLease lease)
    {
        lock (_gate)
        {
            _activeOperationCount--;
            _reservedItemCount -= lease.Request.ReservedItemCount;
            _reservedByteCount -= lease.Request.ReservedByteCount;
            TryAdmitWaiters();
        }
    }

    /// <summary>
    /// 表示一个正在等待准入的请求及其等待状态。
    /// </summary>
    private sealed class Waiter
    {
        /// <summary>
        /// 为一个准入请求创建排队项。
        /// </summary>
        /// <param name="request">对应的准入请求。</param>
        public Waiter(ConcurrencyAdmissionRequest request)
        {
            Request = request;
        }

        public ConcurrencyAdmissionRequest Request { get; }
        public TaskCompletionSource<ConcurrencyAdmissionLease> Completion { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public ConcurrencyAdmissionReason AdmissionReason { get; set; }
        public bool IsCanceled { get; set; }
    }

    /// <summary>
    /// 封装取消注册回调所需的控制器与排队项引用。
    /// </summary>
    private sealed class CancellationRegistrationState
    {
        /// <summary>
        /// 创建一个取消注册状态对象。
        /// </summary>
        /// <param name="controller">拥有排队项的准入控制器。</param>
        /// <param name="waiter">要取消的排队项。</param>
        public CancellationRegistrationState(ConcurrencyAdmissionController controller, Waiter waiter)
        {
            Controller = controller;
            Waiter = waiter;
        }

        public ConcurrencyAdmissionController Controller { get; }
        public Waiter Waiter { get; }

        /// <summary>
        /// 将取消信号转发给对应的准入控制器。
        /// </summary>
        public void Cancel()
        {
            Controller.Cancel(Waiter);
        }
    }

    /// <summary>
    /// 表示一次已获准入的独立操作租约，释放后会归还占用配额。
    /// </summary>
    public sealed class ConcurrencyAdmissionLease : IDisposable
    {
        private ConcurrencyAdmissionController? _controller;

        /// <summary>
        /// 创建一个新的准入租约。
        /// </summary>
        /// <param name="controller">负责释放配额的准入控制器。</param>
        /// <param name="request">当前租约对应的准入请求。</param>
        /// <param name="admissionReason">本次准入命中的原因。</param>
        /// <param name="queueWait">请求在队列中的等待时长。</param>
        internal ConcurrencyAdmissionLease(
            ConcurrencyAdmissionController controller,
            ConcurrencyAdmissionRequest request,
            ConcurrencyAdmissionReason admissionReason,
            TimeSpan queueWait)
        {
            _controller = controller;
            Request = request;
            AdmissionReason = admissionReason;
            QueueWait = queueWait;
        }

        public ConcurrencyAdmissionRequest Request { get; }
        public ConcurrencyAdmissionReason AdmissionReason { get; }
        public TimeSpan QueueWait { get; }

        /// <summary>
        /// 释放当前租约，并把占用的并发与资源配额归还给控制器。
        /// </summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _controller, null)?.Release(this);
        }
    }
}
