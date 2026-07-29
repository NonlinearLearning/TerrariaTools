namespace NL.Concurrency;

/// <summary>
/// 配置有序工作窗口的并发度和已完成结果保留上限。
/// </summary>
/// <param name="MaxDegreeOfParallelism">请求的最大并发度。</param>
/// <param name="ReorderAllowance">允许乱序完成后暂存的结果数量上限。</param>
/// <param name="MaxCompletedRecordCount">允许保留的已完成记录数量上限。</param>
/// <param name="WorkClass">本次工作的准入类别。</param>
/// <param name="EstimatedRetainedBytesPerItem">每个保留项的预估字节数。</param>
public sealed record ConcurrencyWindowOptions(
    int MaxDegreeOfParallelism,
    int? ReorderAllowance = null,
    int MaxCompletedRecordCount = int.MaxValue,
    ConcurrencyWorkClass WorkClass = ConcurrencyWorkClass.Throughput,
    long EstimatedRetainedBytesPerItem = 0)
{
    /// <summary>
    /// 获取至少为一个工作线程的有效并行度。
    /// </summary>
    public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

    /// <summary>
    /// 获取调度暂停前可保留的乱序已完成结果数量上限。
    /// </summary>
    public int EffectiveReorderAllowance =>
        Math.Max(1, ReorderAllowance ?? EffectiveMaxDegreeOfParallelism);

    /// <summary>
    /// 获取已完成工作允许保留的正记录数上限。
    /// </summary>
    public int EffectiveMaxCompletedRecordCount => Math.Max(1, MaxCompletedRecordCount);

    /// <summary>
    /// 为当前窗口配置创建一次准入请求。
    /// </summary>
    /// <param name="sourceCount">本次操作的输入项数量。</param>
    /// <returns>用于并发准入控制的请求对象。</returns>
    public ConcurrencyAdmissionRequest CreateAdmissionRequest(int sourceCount)
    {
        Validate();

        var reservedItemCount = Math.Min(Math.Max(1, sourceCount), EffectiveReorderAllowance);
        var reservedByteCount = checked(EstimatedRetainedBytesPerItem * reservedItemCount);
        return new ConcurrencyAdmissionRequest(WorkClass, reservedItemCount, reservedByteCount);
    }

    /// <summary>
    /// 验证窗口参数是否处于允许范围内。
    /// </summary>
    internal void Validate()
    {
        if (ReorderAllowance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReorderAllowance));
        }

        if (EstimatedRetainedBytesPerItem < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EstimatedRetainedBytesPerItem));
        }
    }
}
