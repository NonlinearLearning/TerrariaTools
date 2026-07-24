using System.Diagnostics;

namespace MinimalRoslynCpg.Builder;

public enum CpgBuildAdmissionPolicy
{
  WholeBuild,
  FairCapped
}

public sealed class CpgBuildAdmissionBudget
{
  private readonly object _gate = new();
  private readonly LinkedList<PendingRequest> _pendingRequests = new();
  private int _availableDegree;
  private int _activeLeaseCount;
  private int _grantedDegreeHighWaterMark;
  private int _grantedDegreeInUse;

  public CpgBuildAdmissionBudget(
    int totalDegree,
    CpgBuildAdmissionPolicy policy = CpgBuildAdmissionPolicy.WholeBuild)
  {
    TotalDegree = Math.Max(1, totalDegree);
    Policy = policy;
    MaxDegreePerLease = policy == CpgBuildAdmissionPolicy.FairCapped
      ? Math.Max(1, TotalDegree / 2)
      : TotalDegree;
    _availableDegree = TotalDegree;
  }

  public int TotalDegree { get; }

  public CpgBuildAdmissionPolicy Policy { get; }

  public int MaxDegreePerLease { get; }

  public int ActiveLeaseCount
  {
    get
    {
      lock (_gate)
      {
        return _activeLeaseCount;
      }
    }
  }

  public int GrantedDegreeInUse
  {
    get
    {
      lock (_gate)
      {
        return _grantedDegreeInUse;
      }
    }
  }

  public int GrantedDegreeHighWaterMark
  {
    get
    {
      lock (_gate)
      {
        return _grantedDegreeHighWaterMark;
      }
    }
  }

  public Task<CpgBuildAdmissionLease> AcquireAsync(
    int requestedDegree,
    CancellationToken cancellationToken,
    CpgBuildAdmissionPolicy? policy = null)
  {
    if (requestedDegree <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(requestedDegree));
    }

    var appliedPolicy = policy ?? Policy;
    var maxDegreePerLease = GetMaxDegreePerLease(appliedPolicy);
    cancellationToken.ThrowIfCancellationRequested();
    var request = new PendingRequest(
      requestedDegree,
      Math.Min(requestedDegree, maxDegreePerLease),
      appliedPolicy,
      maxDegreePerLease,
      cancellationToken);
    request.CancellationRegistration = cancellationToken.Register(
      static state =>
      {
        var (budget, pending) = ((CpgBuildAdmissionBudget Budget, PendingRequest Pending))state!;
        budget.Cancel(pending);
      },
      (this, request));

    List<GrantedRequest>? granted;
    lock (_gate)
    {
      request.Node = _pendingRequests.AddLast(request);
      granted = GrantAvailableRequestsLocked();
    }

    CompleteGrantedRequests(granted);
    return request.Completion.Task;
  }

  private void Cancel(PendingRequest request)
  {
    List<GrantedRequest>? granted = null;
    var cancelled = false;
    lock (_gate)
    {
      if (request.Node?.List is not null)
      {
        _pendingRequests.Remove(request.Node);
        request.Node = null;
        cancelled = true;
        granted = GrantAvailableRequestsLocked();
      }
    }

    if (cancelled)
    {
      request.Completion.TrySetCanceled(request.CancellationToken);
    }

    CompleteGrantedRequests(granted);
  }

  private void Release(CpgBuildAdmissionLease lease)
  {
    List<GrantedRequest>? granted;
    lock (_gate)
    {
      _availableDegree += lease.GrantedDegree;
      _grantedDegreeInUse -= lease.GrantedDegree;
      _activeLeaseCount -= 1;
      granted = GrantAvailableRequestsLocked();
    }

    CompleteGrantedRequests(granted);
  }

  private List<GrantedRequest>? GrantAvailableRequestsLocked()
  {
    List<GrantedRequest>? granted = null;
    while (_pendingRequests.First is { Value: var request })
    {
      if (request.CancellationToken.IsCancellationRequested)
      {
        _pendingRequests.RemoveFirst();
        request.Node = null;
        request.Completion.TrySetCanceled(request.CancellationToken);
        continue;
      }

      if (_availableDegree < request.GrantedDegree)
      {
        break;
      }

      _pendingRequests.RemoveFirst();
      request.Node = null;
      _availableDegree -= request.GrantedDegree;
      _grantedDegreeInUse += request.GrantedDegree;
      _activeLeaseCount += 1;
      _grantedDegreeHighWaterMark = Math.Max(
        _grantedDegreeHighWaterMark,
        _grantedDegreeInUse);
      var lease = new CpgBuildAdmissionLease(
        this,
        request.RequestedDegree,
        request.GrantedDegree,
        request.Policy,
        request.MaxDegreePerLease,
        request.Stopwatch.ElapsedMilliseconds,
        _activeLeaseCount,
        _grantedDegreeHighWaterMark);
      (granted ??= new List<GrantedRequest>()).Add(new GrantedRequest(request, lease));
    }

    return granted;
  }

  private static void CompleteGrantedRequests(List<GrantedRequest>? granted)
  {
    if (granted is null)
    {
      return;
    }

    foreach (var item in granted)
    {
      item.Request.CancellationRegistration.Dispose();
      item.Request.Completion.TrySetResult(item.Lease);
    }
  }

  private int GetMaxDegreePerLease(CpgBuildAdmissionPolicy policy)
  {
    return policy == CpgBuildAdmissionPolicy.FairCapped
      ? Math.Max(1, TotalDegree / 2)
      : TotalDegree;
  }

  private sealed class PendingRequest
  {
    public PendingRequest(
      int requestedDegree,
      int grantedDegree,
      CpgBuildAdmissionPolicy policy,
      int maxDegreePerLease,
      CancellationToken cancellationToken)
    {
      RequestedDegree = requestedDegree;
      GrantedDegree = grantedDegree;
      Policy = policy;
      MaxDegreePerLease = maxDegreePerLease;
      CancellationToken = cancellationToken;
    }

    public int RequestedDegree { get; }

    public int GrantedDegree { get; }

    public CpgBuildAdmissionPolicy Policy { get; }

    public int MaxDegreePerLease { get; }

    public CancellationToken CancellationToken { get; }

    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    public TaskCompletionSource<CpgBuildAdmissionLease> Completion { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationTokenRegistration CancellationRegistration { get; set; }

    public LinkedListNode<PendingRequest>? Node { get; set; }
  }

  private sealed record GrantedRequest(PendingRequest Request, CpgBuildAdmissionLease Lease);

  public sealed class CpgBuildAdmissionLease : IDisposable
  {
    private readonly CpgBuildAdmissionBudget _owner;
    private int _disposed;

    internal CpgBuildAdmissionLease(
      CpgBuildAdmissionBudget owner,
      int requestedDegree,
      int grantedDegree,
      CpgBuildAdmissionPolicy policy,
      int maxDegreePerLease,
      long waitMilliseconds,
      int activeLeaseCountAtGrant,
      int grantedDegreeHighWaterMark)
    {
      _owner = owner;
      RequestedDegree = requestedDegree;
      GrantedDegree = grantedDegree;
      Policy = policy;
      MaxDegreePerLease = maxDegreePerLease;
      WaitMilliseconds = waitMilliseconds;
      ActiveLeaseCountAtGrant = activeLeaseCountAtGrant;
      GrantedDegreeHighWaterMark = grantedDegreeHighWaterMark;
    }

    public int RequestedDegree { get; }

    public int GrantedDegree { get; }

    public CpgBuildAdmissionPolicy Policy { get; }

    public int MaxDegreePerLease { get; }

    public long WaitMilliseconds { get; }

    public int ActiveLeaseCountAtGrant { get; }

    public int GrantedDegreeHighWaterMark { get; }

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
      {
        _owner.Release(this);
      }
    }
  }
}
