namespace NL.Concurrency;

/// <summary>
/// 一次提交。取代 ConcurrencyWindowOptions 与各 API 上的 maxDegreeOfParallelism 参数。
/// </summary>
/// <typeparam name="TResult">本次提交产出的事实类型。</typeparam>
public sealed record WorkSubmission<TResult>
{
    /// <summary>本次提交的全部工作项。</summary>
    public required IReadOnlyList<WorkItem<TResult>> Items { get; init; }

    /// <summary>提交类别，对应 YAML 的一个 execution 字段作用域。</summary>
    public string Category { get; init; } = WorkCategories.Default;

    /// <summary>为 true 时按 StableOrder 归并并返回；为 false 时结果顺序不作保证。</summary>
    public bool PreserveOrder { get; init; } = true;

    /// <summary>覆盖类别在途上限；null 表示使用内核按类别解析的值。</summary>
    public int? MaxConcurrency { get; init; }

    /// <summary>本次提交允许在途的估算字节上限；0 表示不限。</summary>
    public long MaxInFlightBytes { get; init; }
}
