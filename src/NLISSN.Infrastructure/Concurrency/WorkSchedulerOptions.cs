namespace NL.Concurrency;

/// <summary>
/// 内核配额。六个上限字段与 YAML execution 的必填字段一一对应，
/// 使 Schema 2 契约保持不变，而实现从四层塌为单层。
/// </summary>
/// <param name="DirectoryLimit">目录分片窗口内同时存活的文件数上限。</param>
/// <param name="CpgLimit">CPG 工作在途上限。</param>
/// <param name="RuleGroupLimit">规则组阶段在途上限。</param>
/// <param name="HelperLimit">helper 扫描在途上限。</param>
/// <param name="ReplayLimit">回放在途上限。</param>
/// <param name="MaxConcurrentOperations">未分类操作的默认在途上限。</param>
public sealed record WorkSchedulerOptions(
    int DirectoryLimit,
    int CpgLimit,
    int RuleGroupLimit,
    int HelperLimit,
    int ReplayLimit,
    int MaxConcurrentOperations)
{
    /// <summary>目录文件分析是否启用并行；false 时该类别退化为串行。</summary>
    public bool DirectoryParallelism { get; init; } = true;

    /// <summary>规则组阶段是否启用并行。</summary>
    public bool GroupParallelism { get; init; }

    /// <summary>helper 扫描是否启用并行。</summary>
    public bool HelperParallelism { get; init; } = true;

    /// <summary>吞吐项在老化提升前允许等待的最长时间。</summary>
    public TimeSpan MaximumThroughputWait { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 任一类别上限、以及由此推导出的 worker 数 P 的绝对上界。
    /// <para>
    /// 内核为**每个** worker 启动一个长期 <c>Task</c>（<c>WorkScheduler.EnsureWorkersStarted</c>），
    /// 而六个上限在 Schema 2 / YAML 侧只要求 "&gt;= 1"，没有任何上界：
    /// 一个合法配置值就能让首次提交排队上亿个任务。本仓库对同类字段已有封顶先例
    /// （<c>ProjectExportOptions.ProjectWorkerCount</c> 把导出 worker 封顶 12）。
    /// </para>
    /// <para>
    /// 取 1024：远超任何真实机器的核心数，CPU 密集型工作的 DOP 超过核心数本无收益，
    /// 故不会拒绝任何合理配置，同时把资源消耗限制在可承受范围内。
    /// </para>
    /// </summary>
    public const int MaximumWorkerCount = 1024;

    /// <summary>
    /// 校验六个上限均落在 <see cref="MaximumWorkerCount"/> 之内。
    /// 必须在构造内核前调用，使非法配置**可诊断地立即失败**，
    /// 而不是在首次提交时排队上亿个任务。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">某个上限超出上界时抛出，并指出字段名与实际值。</exception>
    public void Validate()
    {
        ValidateLimit(DirectoryLimit, nameof(DirectoryLimit));
        ValidateLimit(CpgLimit, nameof(CpgLimit));
        ValidateLimit(RuleGroupLimit, nameof(RuleGroupLimit));
        ValidateLimit(HelperLimit, nameof(HelperLimit));
        ValidateLimit(ReplayLimit, nameof(ReplayLimit));
        ValidateLimit(MaxConcurrentOperations, nameof(MaxConcurrentOperations));
    }

    private static void ValidateLimit(int value, string fieldName)
    {
        if (value > MaximumWorkerCount)
        {
            throw new ArgumentOutOfRangeException(
              fieldName,
              value,
              $"{fieldName} exceeds the supported maximum of {MaximumWorkerCount}: the kernel starts one " +
              "long-lived task per worker, so a larger limit would exhaust the process before doing any work.");
        }
    }

    /// <summary>
    /// 长期 worker 数量。取全部类别上限的最大值，使任一类别上限都可达成，
    /// 且不新增 YAML 字段（Schema 2 的 execution 为 additionalProperties: false）。
    /// </summary>
    /// <remarks>
    /// 调用 <see cref="Validate"/> 后本值必然 &lt;= <see cref="MaximumWorkerCount"/>；
    /// 未校验的实例仍可能更大，故此处不做隐式截断——截断会静默改变用户的配置意图，
    /// 由 <see cref="Validate"/> 显式拒绝更诚实。
    /// </remarks>
    public int WorkerCount => Math.Max(1, new[]
    {
        DirectoryLimit,
        CpgLimit,
        RuleGroupLimit,
        HelperLimit,
        ReplayLimit,
        MaxConcurrentOperations,
    }.Max());

    /// <summary>解析某类别实际生效的在途上限。</summary>
    public int ResolveLimit(string category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return category switch
        {
            WorkCategories.Directory => DirectoryParallelism ? Math.Max(1, DirectoryLimit) : 1,
            WorkCategories.Cpg => Math.Max(1, CpgLimit),
            WorkCategories.RuleGroup => GroupParallelism ? Math.Max(1, RuleGroupLimit) : 1,
            WorkCategories.Helper => HelperParallelism ? Math.Max(1, HelperLimit) : 1,
            WorkCategories.Replay => Math.Max(1, ReplayLimit),
            WorkCategories.Default => Math.Max(1, MaxConcurrentOperations),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown work category."),
        };
    }

    /// <summary>
    /// 按当前机器核心数构造默认配额。用于没有运行时配额的独立调用方
    /// （例如只构造 <see cref="WorkScheduler"/> 做局部规则图执行的测试）。
    /// </summary>
    public static WorkSchedulerOptions CreateDefault()
    {
        var processorCount = Math.Max(1, Environment.ProcessorCount);
        return new WorkSchedulerOptions(
          DirectoryLimit: processorCount,
          CpgLimit: processorCount,
          RuleGroupLimit: processorCount,
          HelperLimit: processorCount,
          ReplayLimit: processorCount,
          MaxConcurrentOperations: processorCount);
    }
}
