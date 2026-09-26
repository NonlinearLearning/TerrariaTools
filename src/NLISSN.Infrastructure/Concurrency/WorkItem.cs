namespace NL.Concurrency;

/// <summary>
/// 内核调度的一个工作单元。取代 CpgWorkBatch、CpgWorkItem 与 DependencyWorkItem。
/// </summary>
/// <typeparam name="TResult">该项产出的事实类型。</typeparam>
public sealed record WorkItem<TResult>
{
    /// <summary>全局单调的稳定顺序，是结果归并的唯一依据。</summary>
    public required long StableOrder { get; init; }

    /// <summary>
    /// 获得 worker 后执行的工作。第一个参数是本次提交内已完成前置项的结果，
    /// 以 <see cref="StableOrder"/> 为键；无依赖项忽略该参数即可。
    /// 同步 CPU 批应包装为返回已完成 Task。
    /// </summary>
    public required Func<
        IReadOnlyDictionary<long, TResult>,
        CancellationToken,
        Task<TResult>> ExecuteAsync { get; init; }

    /// <summary>必须先行完成的前置项 <see cref="StableOrder"/> 集合；空表示无依赖、立刻就绪。</summary>
    public IReadOnlyList<long> Dependencies { get; init; } = [];

    /// <summary>竞争预算时的服务类别。</summary>
    public WorkPriority Priority { get; init; } = WorkPriority.Throughput;

    /// <summary>CPU 成本估算，用于就绪序与队列预算。</summary>
    public int EstimatedCost { get; init; }

    /// <summary>内存字节估算，用于在途字节背压。</summary>
    public long EstimatedBytes { get; init; }
}
