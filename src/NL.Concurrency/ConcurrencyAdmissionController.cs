using System.Diagnostics;

namespace NL.Concurrency;

/// <summary>
/// Arbitrates independent operation admission within one runtime. It does not
/// reorder work inside an admitted operation.
/// </summary>
public sealed class ConcurrencyAdmissionController
{
    private readonly object _gate = new();
    private readonly ConcurrencyAdmissionOptions _options;
    private readonly Queue<Waiter> _latencySensitiveWaiters = new();
    private readonly Queue<Waiter> _throughputWaiters = new();
    private ConcurrencyWorkClass _nextClass = ConcurrencyWorkClass.LatencySensitive;
    private int _remainingClassCredit;
    private int _activeOperationCount;
    private int _reservedItemCount;
    private long _reservedByteCount;

    public ConcurrencyAdmissionController(ConcurrencyAdmissionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _remainingClassCredit = options.LatencySensitiveWeight;
    }

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

    private async Task<ConcurrencyAdmissionLease> WaitForLeaseAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
          static state => ((CancellationRegistrationState)state!).Cancel(),
          new CancellationRegistrationState(this, waiter));
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private void Enqueue(Waiter waiter)
    {
        GetQueue(waiter.Request.WorkClass).Enqueue(waiter);
    }

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

            var queue = GetQueue(waiter.Request.WorkClass);
            if (!ReferenceEquals(queue.Peek(), waiter))
            {
                throw new InvalidOperationException("Admission queue order was corrupted.");
            }

            queue.Dequeue();
            _activeOperationCount++;
            _reservedItemCount += waiter.Request.ReservedItemCount;
            _reservedByteCount += waiter.Request.ReservedByteCount;
            ConsumeCredit(waiter.Request.WorkClass);
            var lease = new ConcurrencyAdmissionLease(
              this,
              waiter.Request,
              waiter.AdmissionReason,
              waiter.Stopwatch.Elapsed);
            waiter.Completion.TrySetResult(lease);
        }
    }

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
            var queue = GetQueue(_nextClass);
            if (queue.TryPeek(out var waiter) && Fits(waiter.Request))
            {
                waiter.AdmissionReason = ConcurrencyAdmissionReason.WeightedTurn;
                return waiter;
            }

            SwitchClassWithoutSpendingCredit();
        }

        return null;
    }

    private void ConsumeCredit(ConcurrencyWorkClass admittedClass)
    {
        if (admittedClass != _nextClass)
        {
            _nextClass = admittedClass;
            _remainingClassCredit = WeightFor(admittedClass);
        }

        _remainingClassCredit--;
        if (_remainingClassCredit == 0)
        {
            _nextClass = Other(_nextClass);
            _remainingClassCredit = WeightFor(_nextClass);
        }
    }

    private void SwitchClassWithoutSpendingCredit()
    {
        _nextClass = Other(_nextClass);
        _remainingClassCredit = WeightFor(_nextClass);
    }

    private bool Fits(ConcurrencyAdmissionRequest request)
    {
        return _reservedItemCount <= _options.MaxReservedItemCount - request.ReservedItemCount &&
          _reservedByteCount <= _options.MaxReservedByteCount - request.ReservedByteCount;
    }

    private Queue<Waiter> GetQueue(ConcurrencyWorkClass workClass)
    {
        return workClass == ConcurrencyWorkClass.LatencySensitive
          ? _latencySensitiveWaiters
          : _throughputWaiters;
    }

    private int WeightFor(ConcurrencyWorkClass workClass)
    {
        return workClass == ConcurrencyWorkClass.LatencySensitive
          ? _options.LatencySensitiveWeight
          : _options.ThroughputWeight;
    }

    private static ConcurrencyWorkClass Other(ConcurrencyWorkClass workClass)
    {
        return workClass == ConcurrencyWorkClass.LatencySensitive
          ? ConcurrencyWorkClass.Throughput
          : ConcurrencyWorkClass.LatencySensitive;
    }

    private static void DiscardCanceledHeads(Queue<Waiter> queue)
    {
        while (queue.TryPeek(out var waiter) && waiter.IsCanceled)
        {
            queue.Dequeue();
        }
    }

    private void Cancel(Waiter waiter)
    {
        lock (_gate)
        {
            waiter.IsCanceled = true;
            waiter.Completion.TrySetCanceled();
            TryAdmitWaiters();
        }
    }

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

    private sealed class Waiter
    {
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

    private sealed class CancellationRegistrationState
    {
        public CancellationRegistrationState(ConcurrencyAdmissionController controller, Waiter waiter)
        {
            Controller = controller;
            Waiter = waiter;
        }

        public ConcurrencyAdmissionController Controller { get; }
        public Waiter Waiter { get; }

        public void Cancel()
        {
            Controller.Cancel(Waiter);
        }
    }

    public sealed class ConcurrencyAdmissionLease : IDisposable
    {
        private ConcurrencyAdmissionController? _controller;

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

        public void Dispose()
        {
            Interlocked.Exchange(ref _controller, null)?.Release(this);
        }
    }
}
