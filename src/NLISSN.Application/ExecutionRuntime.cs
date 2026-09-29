using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using NL.Caching;
using NL.Concurrency;
using NLCPG.Builder;
using NLISSN.Core.Performance;

namespace NLISSN.Core.Pipeline;

public sealed record RoslynPrototypeExecutionOptions(
  int DirectoryMaxDegreeOfParallelism,
  int CpgMaxDegreeOfParallelism,
  int GroupMaxDegreeOfParallelism,
  int HelperMaxDegreeOfParallelism,
  int ReplayMaxDegreeOfParallelism,
  int MaxConcurrentOperations,
  bool EnableDirectoryParallelism = true,
  bool EnableGroupParallelism = false,
  bool EnableHelperParallelism = true,
  CancellationToken CancellationToken = default)
{
    public int EffectiveDirectoryMaxDegreeOfParallelism =>
      Math.Max(1, DirectoryMaxDegreeOfParallelism);

    public int EffectiveCpgMaxDegreeOfParallelism => Math.Max(1, CpgMaxDegreeOfParallelism);

    public int EffectiveGroupMaxDegreeOfParallelism =>
      Math.Max(1, GroupMaxDegreeOfParallelism);

    public int EffectiveHelperMaxDegreeOfParallelism =>
      Math.Max(1, HelperMaxDegreeOfParallelism);

    public int EffectiveReplayMaxDegreeOfParallelism =>
      Math.Max(1, ReplayMaxDegreeOfParallelism);

    public int EffectiveMaxConcurrentOperations => Math.Max(1, MaxConcurrentOperations);

    // 生成一份基于当前机器核心数的默认执行选项。
    public static RoslynPrototypeExecutionOptions CreateDefault()
    {
        var processorCount = Math.Max(1, Environment.ProcessorCount);
        return new RoslynPrototypeExecutionOptions(
          DirectoryMaxDegreeOfParallelism: processorCount,
          CpgMaxDegreeOfParallelism: processorCount,
          GroupMaxDegreeOfParallelism: processorCount,
          HelperMaxDegreeOfParallelism: processorCount,
          ReplayMaxDegreeOfParallelism: processorCount,
          MaxConcurrentOperations: processorCount);
    }
}

public sealed record AnalysisEpoch(
  int EpochId,
  int SourceVersion,
  int CacheVersion);

public sealed class AnalysisRuntime
{
    private readonly WeakTypedCacheRegistry<Compilation> _cacheRegistry;
    private readonly WorkTelemetryCollector? _workTelemetry;
    private readonly bool _ownsScheduler;
    private readonly AsyncLocal<CpgBuildAdmissionBudget.CpgBuildAdmissionLease?> _currentCpgBuildAdmissionLease = new();

    // 用执行选项、epoch 和可选调度器创建一次分析运行时，并初始化配套缓存与 CPG 准入预算。
    // scheduler 为 null 时由本运行时自建内核，并同时接上内核遥测接收端（见私有构造）。
    public AnalysisRuntime(RoslynPrototypeExecutionOptions executionOptions, AnalysisEpoch epoch, IConcurrencyPool? concurrencyPool = null, WorkScheduler? scheduler = null)
      : this(
        executionOptions,
        epoch,
        concurrencyPool,
        new ConcurrencyOperationTelemetryCollector(),
        new ConcurrencyAdmissionController(CreateConcurrencyAdmissionOptions(executionOptions)),
        new WeakTypedCacheRegistry<Compilation>(),
        new CpgBuildAdmissionBudget(executionOptions.EffectiveCpgMaxDegreeOfParallelism),
        scheduler,
        workTelemetry: null,
        ownsScheduler: scheduler is null)
    {
    }

    private AnalysisRuntime(RoslynPrototypeExecutionOptions executionOptions, AnalysisEpoch epoch, IConcurrencyPool? concurrencyPool, ConcurrencyOperationTelemetryCollector concurrencyTelemetry, ConcurrencyAdmissionController concurrencyAdmissionController, WeakTypedCacheRegistry<Compilation> cacheRegistry, CpgBuildAdmissionBudget cpgBuildAdmissionBudget, WorkScheduler? scheduler, WorkTelemetryCollector? workTelemetry, bool ownsScheduler)
    {
        _ownsScheduler = ownsScheduler;
        ExecutionOptions = executionOptions;
        Epoch = epoch;
        ConcurrencyPool = concurrencyPool ?? new BoundedConcurrencyPool(concurrencyTelemetry, concurrencyAdmissionController);
        ConcurrencyTelemetry = concurrencyTelemetry;
        ConcurrencyAdmissionController = concurrencyAdmissionController;
        _cacheRegistry = cacheRegistry;
        CpgBuildAdmissionBudget = cpgBuildAdmissionBudget;

        if (scheduler is null)
        {
            // 自建内核：必须在此处接上接收端，否则内核成为遥测黑洞
            // （提交已发生后再补挂会漏掉全部记录）。
            _workTelemetry = workTelemetry ?? new WorkTelemetryCollector();
            Scheduler = new WorkScheduler(CreateSchedulerOptions(executionOptions), _workTelemetry);
        }
        else
        {
            // 注入内核：其接收端由注入方拥有，本运行时**不作任何遥测声明**（保持为 null）。
            _workTelemetry = workTelemetry;
            Scheduler = scheduler;
        }
    }

    public RoslynPrototypeExecutionOptions ExecutionOptions { get; }

    public AnalysisEpoch Epoch { get; }

    public IConcurrencyPool ConcurrencyPool { get; }

    public ConcurrencyOperationTelemetryCollector ConcurrencyTelemetry { get; }

    public ConcurrencyAdmissionController ConcurrencyAdmissionController { get; }

    public CpgBuildAdmissionBudget CpgBuildAdmissionBudget { get; }

    /// <summary>
    /// 内核遥测接收端。仅当内核由本运行时自建时非空；
    /// 调用方注入内核时其接收端归注入方所有，这里保持 null（不冒充拥有者）。
    /// </summary>
    public WorkTelemetryCollector? WorkTelemetry => _workTelemetry;

    /// <summary>
    /// 全 run 唯一的调度内核。拥有长期 worker，故派生 runtime 必须复用它。
    /// </summary>
    public WorkScheduler Scheduler { get; }

    /// <summary>
    /// 内核配额。直接取自 <see cref="Scheduler"/> 正在使用的实例，
    /// 而不是把 <see cref="ExecutionOptions"/> 再映射一次：允许注入 scheduler 时，
    /// 二者可能不同，报告「生效值」必须以后者为准。
    /// </summary>
    public WorkSchedulerOptions SchedulerOptions => Scheduler.Options;

    public CpgBuildAdmissionBudget.CpgBuildAdmissionLease? CurrentCpgBuildAdmissionLease =>
      _currentCpgBuildAdmissionLease.Value;

    /// <summary>
    /// 本运行时是否拥有 <see cref="Scheduler"/> 的生命周期。
    /// </summary>
    /// <remarks>
    /// 仅当内核是**本运行时自建**（构造函数未注入 scheduler）时为 true；
    /// 注入实例归注入方、派生 runtime 共享同一实例，二者均为 false。
    /// 释放时必须据此判断，否则会提前关掉别人的内核（借用方不得 Dispose）。
    /// </remarks>
    public bool OwnsScheduler => _ownsScheduler;

    /// <summary>
    /// 释放本运行时自建的内核；借用与派生实例一律不动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WorkScheduler.DisposeAsync"/> 的语义是**排空**而非取消：
    /// 它停止接受新提交，等待已接受的工作结束，再结束全部长期 worker。
    /// 故这里在成功、首次异常与外部取消三条路径上都可以安全调用。
    /// </para>
    /// <para>
    /// 不实现 <see cref="IAsyncDisposable"/> 是刻意的：把 <c>await using</c> 直接引入
    /// <c>CommandHost</c> 会改变既有的释放顺序与异常语义（运行日志、性能摘要都在
    /// <c>finally</c> 之后才收尾）。改由调用方在收尾完成后显式调用本方法，接入面更小。
    /// </para>
    /// </remarks>
    public async ValueTask DisposeSchedulerAsync()
    {
        if (!_ownsScheduler)
        {
            return;
        }

        await Scheduler.DisposeAsync().ConfigureAwait(false);
    }

    public IPerformanceEventSink? PerformanceEventSink { get; set; }

    public IPartitionPerformanceEventSink? PartitionPerformanceEventSink { get; set; }

    public IPerformanceStageCollector? PerformanceStageCollector { get; set; }

    public string? PerformanceRunId { get; set; }

    /// <summary>
    /// 跨全部 CPG 构建汇总 per-worker 使用率的接收端；为 <c>null</c> 时不产生记账开销。
    /// </summary>
    /// <remarks>
    /// 放在 runtime 上而非 builder 上：NLISSN 对每个源文件新建 builder，
    /// 只有 run 级对象才能把 967 个池的记账累加到同一份报告里。
    /// </remarks>
    public CpgWorkerUtilizationCollector? CpgWorkerUtilizationCollector { get; set; }

    public string CacheScopeKey => $"epoch:{Epoch.EpochId}|cache:{Epoch.CacheVersion}";

    // 创建一份默认运行时，适合单次分析或未显式传参的调用路径。
    public static AnalysisRuntime CreateDefault()
    {
        return new AnalysisRuntime(
          RoslynPrototypeExecutionOptions.CreateDefault(),
          new AnalysisEpoch(0, 0, 0));
    }

    // 在保留编译缓存注册表的前提下推进缓存版本，隔离失效后的结构视图。
    public AnalysisRuntime InvalidateCaches()
    {
        // 保持同一注册表以复用仍有效的编译缓存，只改变作用域键以隔离失效后的视图。
        var runtime = new AnalysisRuntime(
          ExecutionOptions,
          Epoch with { CacheVersion = Epoch.CacheVersion + 1 },
          ConcurrencyPool,
          ConcurrencyTelemetry,
          ConcurrencyAdmissionController,
          _cacheRegistry,
          CpgBuildAdmissionBudget,
          Scheduler,
          _workTelemetry,
          // 派生 runtime 复用同一内核实例，故**不**取得所有权：
          // 提前释放会让原 runtime 的内核在后续阶段不可用。
          ownsScheduler: false);
        runtime.PerformanceEventSink = PerformanceEventSink;
        runtime.PartitionPerformanceEventSink = PartitionPerformanceEventSink;
        runtime.PerformanceStageCollector = PerformanceStageCollector;
        runtime.PerformanceRunId = PerformanceRunId;
        // 刻意共享同一汇总端：派生 runtime 的构建也要计入同一份 run 级报告。
        runtime.CpgWorkerUtilizationCollector = CpgWorkerUtilizationCollector;
        return runtime;
    }

    // 在源码轮次变更后同时推进 epoch、source 和 cache 版本，阻止旧事实串用。
    public AnalysisRuntime NextEpoch()
    {
        // 源文本变更后同时推进来源和缓存版本，禁止跨分析轮次复用结构事实。
        var runtime = new AnalysisRuntime(
          ExecutionOptions,
          new AnalysisEpoch(
            Epoch.EpochId + 1,
            Epoch.SourceVersion + 1,
            Epoch.CacheVersion + 1),
          ConcurrencyPool,
          ConcurrencyTelemetry,
          ConcurrencyAdmissionController,
          _cacheRegistry,
          CpgBuildAdmissionBudget,
          Scheduler,
          _workTelemetry,
          // 同上：派生实例共享内核，不取得所有权。
          ownsScheduler: false);
        runtime.PerformanceEventSink = PerformanceEventSink;
        runtime.PartitionPerformanceEventSink = PartitionPerformanceEventSink;
        runtime.PerformanceStageCollector = PerformanceStageCollector;
        runtime.PerformanceRunId = PerformanceRunId;
        runtime.CpgWorkerUtilizationCollector = CpgWorkerUtilizationCollector;
        return runtime;
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

    private static ConcurrencyAdmissionOptions CreateConcurrencyAdmissionOptions(
        RoslynPrototypeExecutionOptions executionOptions)
    {
        var maximumParallelism = executionOptions.EffectiveMaxConcurrentOperations;

        // 预留项上限 = 并发上限 × 2。这里显式检测溢出并给出**可诊断**的参数错误：
        // YAML 校验与 JSON Schema 对该字段都只要求 >= 1、没有上界，故极大值是
        // 「通过校验的合法配置」，不该以裸 OverflowException 暴露给用户。
        if (maximumParallelism > int.MaxValue / 2)
        {
            throw new ArgumentOutOfRangeException(
              nameof(executionOptions),
              maximumParallelism,
              "maxConcurrentOperations is too large: the admission reservation (twice the limit) would overflow Int32.");
        }

        return new ConcurrencyAdmissionOptions(
          MaxConcurrentOperations: maximumParallelism,
          MaxReservedItemCount: maximumParallelism * 2,
          MaxReservedByteCount: 128L * 1024 * 1024);
    }

    // 把 6 个执行额度与 3 个并行开关映射到内核配额，保持其余层不变。
    internal static WorkSchedulerOptions CreateSchedulerOptions(RoslynPrototypeExecutionOptions executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        return new WorkSchedulerOptions(
          DirectoryLimit: executionOptions.DirectoryMaxDegreeOfParallelism,
          CpgLimit: executionOptions.CpgMaxDegreeOfParallelism,
          RuleGroupLimit: executionOptions.GroupMaxDegreeOfParallelism,
          HelperLimit: executionOptions.HelperMaxDegreeOfParallelism,
          ReplayLimit: executionOptions.ReplayMaxDegreeOfParallelism,
          MaxConcurrentOperations: executionOptions.MaxConcurrentOperations)
        {
            DirectoryParallelism = executionOptions.EnableDirectoryParallelism,
            GroupParallelism = executionOptions.EnableGroupParallelism,
            HelperParallelism = executionOptions.EnableHelperParallelism,
        };
    }
}
