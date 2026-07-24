using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Builder;

namespace Rules;

public sealed record RoslynPrototypeExecutionOptions(
  int MaxDegreeOfParallelism,
  bool EnableDirectoryParallelism = true,
  bool EnableGroupParallelism = false,
  bool EnableHelperParallelism = true,
  CancellationToken CancellationToken = default,
  int? CpgMaxDegreeOfParallelism = null)
{
  public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

  public int EffectiveCpgMaxDegreeOfParallelism =>
    CpgMaxDegreeOfParallelism ?? EffectiveMaxDegreeOfParallelism;

  public static RoslynPrototypeExecutionOptions CreateDefault()
  {
    return new RoslynPrototypeExecutionOptions(Math.Max(1, Environment.ProcessorCount));
  }
}

public sealed record DeletionAnalysisEpoch(
  int EpochId,
  int SourceVersion,
  int CacheVersion);

public interface IRuleStageScheduler
{
  Task<IReadOnlyList<TResult>> RunOrderedAsync<TResult>(
    int itemCount,
    int maxDegreeOfParallelism,
    Func<int, CancellationToken, Task<TResult>> workItem,
    CancellationToken cancellationToken);
}

public sealed class BoundedRuleStageScheduler : IRuleStageScheduler
{
  public async Task<IReadOnlyList<TResult>> RunOrderedAsync<TResult>(
    int itemCount,
    int maxDegreeOfParallelism,
    Func<int, CancellationToken, Task<TResult>> workItem,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(workItem);
    if (itemCount < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(itemCount));
    }

    if (itemCount == 0)
    {
      return Array.Empty<TResult>();
    }

    if (Math.Max(1, maxDegreeOfParallelism) == 1)
    {
      var serialResults = new TResult[itemCount];
      for (var index = 0; index < itemCount; index++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        serialResults[index] = await workItem(index, cancellationToken);
      }

      return serialResults;
    }

    var results = new TResult[itemCount];
    var nextIndex = -1;
    var workerCount = Math.Min(itemCount, Math.Max(1, maxDegreeOfParallelism));
    var workers = new Task[workerCount];
    for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
    {
      workers[workerIndex] = Task.Run(async () =>
      {
        while (true)
        {
          var index = Interlocked.Increment(ref nextIndex);
          if (index >= itemCount)
          {
            return;
          }

          cancellationToken.ThrowIfCancellationRequested();
          results[index] = await workItem(index, cancellationToken);
        }
      }, cancellationToken);
    }

    await Task.WhenAll(workers);
    return results;
  }
}

public sealed class DeletionAnalysisRuntime
{
  private readonly RuntimeCacheRegistry _cacheRegistry;
  private readonly AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> _currentCpgBuildAdmissionLease = new();

  public DeletionAnalysisRuntime(
    RoslynPrototypeExecutionOptions executionOptions,
    DeletionAnalysisEpoch epoch,
    IRuleStageScheduler? scheduler = null)
    : this(
      executionOptions,
      epoch,
      scheduler,
      new RuntimeCacheRegistry(),
      new CpgBuildAdmissionBudget(
        executionOptions.EffectiveCpgMaxDegreeOfParallelism,
        CpgBuildAdmissionPolicy.FairCapped))
  {
  }

  private DeletionAnalysisRuntime(
    RoslynPrototypeExecutionOptions executionOptions,
    DeletionAnalysisEpoch epoch,
    IRuleStageScheduler? scheduler,
    RuntimeCacheRegistry cacheRegistry,
    CpgBuildAdmissionBudget cpgBuildAdmissionBudget)
  {
    ExecutionOptions = executionOptions;
    Epoch = epoch;
    Scheduler = scheduler ?? new BoundedRuleStageScheduler();
    _cacheRegistry = cacheRegistry;
    CpgBuildAdmissionBudget = cpgBuildAdmissionBudget;
  }

  public RoslynPrototypeExecutionOptions ExecutionOptions { get; }

  public DeletionAnalysisEpoch Epoch { get; }

  public IRuleStageScheduler Scheduler { get; }

  public CpgBuildAdmissionBudget CpgBuildAdmissionBudget { get; }

  public CpgBuildAdmissionBudget.CpgBuildAdmissionLease? CurrentCpgBuildAdmissionLease =>
    _currentCpgBuildAdmissionLease.Value;

  public string CacheScopeKey => $"epoch:{Epoch.EpochId}|cache:{Epoch.CacheVersion}";

  public static DeletionAnalysisRuntime CreateDefault()
  {
    return new DeletionAnalysisRuntime(
      RoslynPrototypeExecutionOptions.CreateDefault(),
      new DeletionAnalysisEpoch(0, 0, 0));
  }

  public static RoslynPrototypeExecutionOptions CreateExecutionOptions(
    IReadOnlyDictionary<string, string> options)
  {
    ArgumentNullException.ThrowIfNull(options);

    return new RoslynPrototypeExecutionOptions(
      ResolveMaxDegreeOfParallelism(options),
      EnableDirectoryParallelism: !IsTrueOption(options, "disable-directory-parallelism"),
      EnableGroupParallelism: IsTrueOption(options, "enable-group-parallelism"),
      EnableHelperParallelism: !IsTrueOption(options, "disable-helper-parallelism"),
      CpgMaxDegreeOfParallelism: ResolveCpgMaxDegreeOfParallelism(options));
  }

  public static DeletionAnalysisRuntime CreateFromOptions(
    IReadOnlyDictionary<string, string> options)
  {
    return new DeletionAnalysisRuntime(
      CreateExecutionOptions(options),
      new DeletionAnalysisEpoch(0, 0, 0));
  }

  public DeletionAnalysisRuntime InvalidateCaches()
  {
    return new DeletionAnalysisRuntime(
      ExecutionOptions,
      Epoch with { CacheVersion = Epoch.CacheVersion + 1 },
      Scheduler,
      _cacheRegistry,
      CpgBuildAdmissionBudget);
  }

  public DeletionAnalysisRuntime NextEpoch()
  {
    return new DeletionAnalysisRuntime(
      ExecutionOptions,
      new DeletionAnalysisEpoch(
        Epoch.EpochId + 1,
        Epoch.SourceVersion + 1,
        Epoch.CacheVersion + 1),
      Scheduler,
      _cacheRegistry,
      CpgBuildAdmissionBudget);
  }

  public IDisposable PushCpgBuildAdmissionLease(
    CpgBuildAdmissionBudget.CpgBuildAdmissionLease lease)
  {
    ArgumentNullException.ThrowIfNull(lease);
    var previous = _currentCpgBuildAdmissionLease.Value;
    _currentCpgBuildAdmissionLease.Value = lease;
    return new CpgBuildAdmissionLeaseScope(_currentCpgBuildAdmissionLease, previous);
  }

  internal TCache GetOrCreateCompilationCache<TCache>(
    Compilation compilation,
    Func<Compilation, TCache> factory)
    where TCache : class
  {
    return _cacheRegistry.GetOrCreateCompilationCache(compilation, factory);
  }

  private sealed class RuntimeCacheRegistry
  {
    private readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<Type, object>> _compilationCaches = new();

    public TCache GetOrCreateCompilationCache<TCache>(
      Compilation compilation,
      Func<Compilation, TCache> factory)
      where TCache : class
    {
      var compilationCaches = _compilationCaches.GetValue(
        compilation,
        static _ => new ConcurrentDictionary<Type, object>());
      return (TCache)compilationCaches.GetOrAdd(typeof(TCache), _ => factory(compilation));
    }
  }

  private sealed class CpgBuildAdmissionLeaseScope : IDisposable
  {
    private readonly AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> _lease;
    private readonly CpgBuildAdmissionBudget.CpgBuildAdmissionLease? _previous;
    private int _disposed;

    public CpgBuildAdmissionLeaseScope(
      AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> lease,
      CpgBuildAdmissionBudget.CpgBuildAdmissionLease? previous)
    {
      _lease = lease;
      _previous = previous;
    }

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) == 0)
      {
        _lease.Value = _previous;
      }
    }
  }

  private static bool IsTrueOption(IReadOnlyDictionary<string, string> options, string key)
  {
    return options.TryGetValue(key, out var rawValue) &&
      string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
  }

  private static int ResolveMaxDegreeOfParallelism(IReadOnlyDictionary<string, string> options)
  {
    if (!options.TryGetValue("max-degree-of-parallelism", out var rawValue) ||
        string.IsNullOrWhiteSpace(rawValue))
    {
      return Math.Max(1, Environment.ProcessorCount);
    }

    if (!int.TryParse(rawValue, out var parsedValue))
    {
      return Math.Max(1, Environment.ProcessorCount);
    }

    return Math.Max(1, parsedValue);
  }

  private static int? ResolveCpgMaxDegreeOfParallelism(
    IReadOnlyDictionary<string, string> options)
  {
    if (!options.TryGetValue("cpg-max-degree-of-parallelism", out var rawValue))
    {
      return null;
    }

    if (!int.TryParse(rawValue, out var parsedValue) || parsedValue <= 0)
    {
      throw new ArgumentException(
        "--cpg-max-degree-of-parallelism requires a positive integer.");
    }

    return parsedValue;
  }
}
