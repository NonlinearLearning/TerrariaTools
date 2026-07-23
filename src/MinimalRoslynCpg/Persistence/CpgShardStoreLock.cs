namespace MinimalRoslynCpg.Persistence;

using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

public sealed class CpgShardStoreLockedException : InvalidOperationException
{
  public CpgShardStoreLockedException(string storeRoot, Exception innerException)
    : base($"The CPG shard store '{storeRoot}' already has an active writer.", innerException)
  {
  }
}

public sealed class CpgShardStoreLock : IDisposable
{
  private static readonly ConcurrentDictionary<string, byte> ActiveStoreLocks = new(StringComparer.Ordinal);
  private readonly Semaphore _semaphore;
  private readonly string _storeKey;

  private CpgShardStoreLock(Semaphore semaphore, string storeKey)
  {
    _semaphore = semaphore;
    _storeKey = storeKey;
  }

  public static CpgShardStoreLock Acquire(string storeRoot)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
    Directory.CreateDirectory(storeRoot);
    var storeKey = Path.GetFullPath(storeRoot);
    if (!ActiveStoreLocks.TryAdd(storeKey, 0))
    {
      throw new CpgShardStoreLockedException(storeRoot, new InvalidOperationException("The store already has an active writer in this process."));
    }

    var lockName = $"MinimalRoslynCpg-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storeKey)))}";
    var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, lockName);
    try
    {
      if (!semaphore.WaitOne(0))
      {
        semaphore.Dispose();
        ActiveStoreLocks.TryRemove(storeKey, out _);
        throw new CpgShardStoreLockedException(storeRoot, new TimeoutException("The store mutex is already held."));
      }

      return new CpgShardStoreLock(semaphore, storeKey);
    }
    catch
    {
      semaphore.Dispose();
      ActiveStoreLocks.TryRemove(storeKey, out _);
      throw;
    }
  }

  public static Task<CpgShardStoreLock> AcquireAsync(
    string storeRoot,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
    if (timeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    Directory.CreateDirectory(storeRoot);
    var storeKey = Path.GetFullPath(storeRoot);
    var lockName = $"MinimalRoslynCpg-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storeKey)))}";
    var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, lockName);
    return new AsyncAcquireWaiter(semaphore, storeRoot, storeKey, timeout, cancellationToken).Start();
  }

  public void Dispose()
  {
    ActiveStoreLocks.TryRemove(_storeKey, out _);
    _semaphore.Release();
    _semaphore.Dispose();
  }

  private static CpgShardStoreLock AcquireWaiting(
    string storeRoot,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(storeRoot);
    var storeKey = Path.GetFullPath(storeRoot);
    var lockName = $"MinimalRoslynCpg-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storeKey)))}";
    var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, lockName);
    try
    {
      var signaled = WaitHandle.WaitAny(
        new WaitHandle[] { semaphore, cancellationToken.WaitHandle }, timeout);
      if (signaled == 1)
      {
        throw new OperationCanceledException(cancellationToken);
      }

      if (signaled == WaitHandle.WaitTimeout)
      {
        throw new TimeoutException($"Timed out waiting for CPG shard store '{storeRoot}'.");
      }

      if (!ActiveStoreLocks.TryAdd(storeKey, 0))
      {
        semaphore.Release();
        throw new CpgShardStoreLockedException(storeRoot,
          new InvalidOperationException("The store already has an active writer in this process."));
      }

      return new CpgShardStoreLock(semaphore, storeKey);
    }
    catch
    {
      semaphore.Dispose();
      throw;
    }
  }

  private sealed class AsyncAcquireWaiter
  {
    private readonly Semaphore _semaphore;
    private readonly string _storeRoot;
    private readonly string _storeKey;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<CpgShardStoreLock> _completion = new(
      TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEvent _waitCallbacksCompleted = new(initialState: false);
    private RegisteredWaitHandle? _registeredWait;
    private RegisteredWaitHandle? _cleanupRegistration;
    private CancellationTokenRegistration _cancellationRegistration;
    private int _completed;
    private int _cleanupScheduled;
    private int _resourcesDisposed;

    internal AsyncAcquireWaiter(
      Semaphore semaphore,
      string storeRoot,
      string storeKey,
      TimeSpan timeout,
      CancellationToken cancellationToken)
    {
      _semaphore = semaphore;
      _storeRoot = storeRoot;
      _storeKey = storeKey;
      _timeout = timeout;
      _cancellationToken = cancellationToken;
    }

    internal Task<CpgShardStoreLock> Start()
    {
      _registeredWait = ThreadPool.RegisterWaitForSingleObject(
        _semaphore,
        static (state, timedOut) => ((AsyncAcquireWaiter)state!).OnWaitCompleted(timedOut),
        this,
        _timeout,
        executeOnlyOnce: true);
      _cancellationRegistration = _cancellationToken.Register(
        static state => ((AsyncAcquireWaiter)state!).Cancel(),
        this);
      return _completion.Task;
    }

    private void OnWaitCompleted(bool timedOut)
    {
      if (timedOut)
      {
        CompleteFailure(
          new TimeoutException($"Timed out waiting for CPG shard store '{_storeRoot}'."),
          fromWaitCallback: true);
        DisposeResources();
        return;
      }

      if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
      {
        _semaphore.Release();
        DisposeResources();
        return;
      }

      if (!ActiveStoreLocks.TryAdd(_storeKey, 0))
      {
        _semaphore.Release();
        _completion.TrySetException(new CpgShardStoreLockedException(
          _storeRoot,
          new InvalidOperationException("The store already has an active writer in this process.")));
        DisposeResources();
        return;
      }

      _cancellationRegistration.Dispose();
      _waitCallbacksCompleted.Dispose();
      _completion.TrySetResult(new CpgShardStoreLock(_semaphore, _storeKey));
    }

    private void Cancel()
    {
      CompleteFailure(new OperationCanceledException(_cancellationToken));
    }

    private void CompleteFailure(Exception exception, bool fromWaitCallback = false)
    {
      if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
      {
        return;
      }

      _completion.TrySetException(exception);
      if (fromWaitCallback)
      {
        return;
      }

      if (_registeredWait is null)
      {
        DisposeResources();
        return;
      }

      if (_registeredWait.Unregister(_waitCallbacksCompleted))
      {
        DisposeResourcesAfterWaitCallbacksComplete();
      }
    }

    private void DisposeResourcesAfterWaitCallbacksComplete()
    {
      if (Interlocked.Exchange(ref _cleanupScheduled, 1) != 0)
      {
        return;
      }

      _cleanupRegistration = ThreadPool.RegisterWaitForSingleObject(
        _waitCallbacksCompleted,
        static (state, _) => ((AsyncAcquireWaiter)state!).DisposeResources(),
        this,
        Timeout.InfiniteTimeSpan,
        executeOnlyOnce: true);
    }

    private void DisposeResources()
    {
      if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
      {
        return;
      }

      _cancellationRegistration.Dispose();
      _semaphore.Dispose();
      _waitCallbacksCompleted.Dispose();
    }
  }
}
