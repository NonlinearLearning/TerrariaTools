using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using NL.Caching;
using NL.Concurrency;
using NLCPG.Builder;

namespace NLISSN.Core.Pipeline;

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

    // 生成一份基于当前机器核心数的默认执行选项。
    public static RoslynPrototypeExecutionOptions CreateDefault()
    {
        return new RoslynPrototypeExecutionOptions(Math.Max(1, Environment.ProcessorCount));
    }
}

public sealed record AnalysisEpoch(
  int EpochId,
  int SourceVersion,
  int CacheVersion);

public sealed class AnalysisRuntime
{
    private readonly WeakTypedCacheRegistry<Compilation> _cacheRegistry;
    private readonly AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> _currentCpgBuildAdmissionLease = new();

    // 用执行选项、epoch 和可选调度器创建一次分析运行时，并初始化配套缓存与 CPG 准入预算。
    public AnalysisRuntime(RoslynPrototypeExecutionOptions executionOptions, AnalysisEpoch epoch, IConcurrencyPool? concurrencyPool = null)
      : this(
        executionOptions,
        epoch,
        concurrencyPool,
        new ConcurrencyOperationTelemetryCollector(),
        new ConcurrencyAdmissionController(CreateConcurrencyAdmissionOptions(executionOptions)),
        new WeakTypedCacheRegistry<Compilation>(),
        new CpgBuildAdmissionBudget(
          executionOptions.EffectiveCpgMaxDegreeOfParallelism,
          CpgBuildAdmissionPolicy.FairCapped))
    {
    }

    private AnalysisRuntime(RoslynPrototypeExecutionOptions executionOptions, AnalysisEpoch epoch, IConcurrencyPool? concurrencyPool, ConcurrencyOperationTelemetryCollector concurrencyTelemetry, ConcurrencyAdmissionController concurrencyAdmissionController, WeakTypedCacheRegistry<Compilation> cacheRegistry, CpgBuildAdmissionBudget cpgBuildAdmissionBudget)
    {
        ExecutionOptions = executionOptions;
        Epoch = epoch;
        ConcurrencyPool = concurrencyPool ?? new BoundedConcurrencyPool(concurrencyTelemetry, concurrencyAdmissionController);
        ConcurrencyTelemetry = concurrencyTelemetry;
        ConcurrencyAdmissionController = concurrencyAdmissionController;
        _cacheRegistry = cacheRegistry;
        CpgBuildAdmissionBudget = cpgBuildAdmissionBudget;
    }

    public RoslynPrototypeExecutionOptions ExecutionOptions { get; }

    public AnalysisEpoch Epoch { get; }

    public IConcurrencyPool ConcurrencyPool { get; }

    public ConcurrencyOperationTelemetryCollector ConcurrencyTelemetry { get; }

    public ConcurrencyAdmissionController ConcurrencyAdmissionController { get; }

    public CpgBuildAdmissionBudget CpgBuildAdmissionBudget { get; }

    public CpgBuildAdmissionBudget.CpgBuildAdmissionLease? CurrentCpgBuildAdmissionLease =>
      _currentCpgBuildAdmissionLease.Value;

    public string CacheScopeKey => $"epoch:{Epoch.EpochId}|cache:{Epoch.CacheVersion}";

    // 创建一份默认运行时，适合单次分析或未显式传参的调用路径。
    public static AnalysisRuntime CreateDefault()
    {
        return new AnalysisRuntime(
          RoslynPrototypeExecutionOptions.CreateDefault(),
          new AnalysisEpoch(0, 0, 0));
    }

    // 从 CLI 选项解析目录并行、分组并行和 CPG 并行等运行参数。
    public static RoslynPrototypeExecutionOptions CreateExecutionOptions(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RoslynPrototypeExecutionOptions(
          ResolveMaxDegreeOfParallelism(options),
          EnableDirectoryParallelism: !IsTrueOption(options, "disable-directory-parallelism"),
          EnableGroupParallelism: IsTrueOption(options, "enable-group-parallelism"),
          EnableHelperParallelism: !IsTrueOption(options, "disable-helper-parallelism"),
          CpgMaxDegreeOfParallelism: ResolveCpgMaxDegreeOfParallelism(options));
    }

    // 从 CLI 选项直接创建运行时，供宿主和应用服务共享同一解析逻辑。
    public static AnalysisRuntime CreateFromOptions(IReadOnlyDictionary<string, string> options)
    {
        return new AnalysisRuntime(
          CreateExecutionOptions(options),
          new AnalysisEpoch(0, 0, 0));
    }

    // 在保留编译缓存注册表的前提下推进缓存版本，隔离失效后的结构视图。
    public AnalysisRuntime InvalidateCaches()
    {
        // 保持同一注册表以复用仍有效的编译缓存，只改变作用域键以隔离失效后的视图。
        return new AnalysisRuntime(
          ExecutionOptions,
          Epoch with { CacheVersion = Epoch.CacheVersion + 1 },
          ConcurrencyPool,
          ConcurrencyTelemetry,
          ConcurrencyAdmissionController,
          _cacheRegistry,
          CpgBuildAdmissionBudget);
    }

    // 在源码轮次变更后同时推进 epoch、source 和 cache 版本，阻止旧事实串用。
    public AnalysisRuntime NextEpoch()
    {
        // 源文本变更后同时推进来源和缓存版本，禁止跨分析轮次复用结构事实。
        return new AnalysisRuntime(
          ExecutionOptions,
          new AnalysisEpoch(
            Epoch.EpochId + 1,
            Epoch.SourceVersion + 1,
            Epoch.CacheVersion + 1),
          ConcurrencyPool,
          ConcurrencyTelemetry,
          ConcurrencyAdmissionController,
          _cacheRegistry,
          CpgBuildAdmissionBudget);
    }

    // 把当前线程的 CPG 构建准入租约压入 AsyncLocal，供下游构图代码读取。
    public IDisposable PushCpgBuildAdmissionLease(CpgBuildAdmissionBudget.CpgBuildAdmissionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var previous = _currentCpgBuildAdmissionLease.Value;
        _currentCpgBuildAdmissionLease.Value = lease;
        return new CpgBuildAdmissionLeaseScope(_currentCpgBuildAdmissionLease, previous);
    }

    // 为指定编译挂接并复用类型级缓存实例，避免同一 compilation 上重复构建辅助事实。
    public TCache GetOrCreateCompilationCache<TCache>(Compilation compilation, Func<Compilation, TCache> factory)
      where TCache : class
    {
        return _cacheRegistry.GetOrCreate(compilation, factory);
    }

    public TCache GetOrCreateEpochCompilationCache<TCache>(Compilation compilation, Func<Compilation, TCache> factory)
      where TCache : class
    {
        var cache = _cacheRegistry.GetOrCreate(
          compilation,
          static _ => new EpochCompilationCache<TCache>());
        return cache.GetOrCreate(CacheScopeKey, compilation, factory);
    }

    private sealed class EpochCompilationCache<TCache>
      where TCache : class
    {
        private readonly ConcurrentDictionary<string, Lazy<TCache>> _values = new(StringComparer.Ordinal);

        public TCache GetOrCreate(string scopeKey, Compilation compilation, Func<Compilation, TCache> factory)
        {
            return _values.GetOrAdd(
              scopeKey,
              _ => new Lazy<TCache>(
                () => factory(compilation),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
    }

    private sealed class CpgBuildAdmissionLeaseScope : IDisposable
    {
        private readonly AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> _lease;
        private readonly CpgBuildAdmissionBudget.CpgBuildAdmissionLease? _previous;
        private int _disposed;

        public CpgBuildAdmissionLeaseScope(AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> lease, CpgBuildAdmissionBudget.CpgBuildAdmissionLease? previous)
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

    private static int? ResolveCpgMaxDegreeOfParallelism(IReadOnlyDictionary<string, string> options)
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

    private static ConcurrencyAdmissionOptions CreateConcurrencyAdmissionOptions(
        RoslynPrototypeExecutionOptions executionOptions)
    {
        var maximumParallelism = executionOptions.EffectiveMaxDegreeOfParallelism;
        return new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: maximumParallelism,
          MaxReservedItemCount: checked(maximumParallelism * 2),
          MaxReservedByteCount: 128L * 1024 * 1024);
    }
}
