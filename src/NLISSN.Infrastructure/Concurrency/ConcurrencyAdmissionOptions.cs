namespace NL.Concurrency;

/// <summary>
/// 定义单个运行时本地准入协调器的资源边界。各项配额显式公开，避免策略隐藏在调度实现里。
/// </summary>
/// <param name="MaxConcurrentOperations">允许同时准入的独立操作上限。</param>
/// <param name="MaxReservedItemCount">所有已准入操作可累计预留的项目数量上限。</param>
/// <param name="MaxReservedByteCount">所有已准入操作可累计预留的字节数量上限。</param>
/// <param name="LatencySensitiveWeight">低延迟工作类别在轮转中的默认权重。</param>
/// <param name="ThroughputWeight">吞吐工作类别在轮转中的默认权重。</param>
/// <param name="MaximumThroughputWait">吞吐工作在老化升级前允许等待的最长时间。</param>
public sealed record ConcurrencyAdmissionOptions(
    int MaxConcurrentOperations,
    int MaxReservedItemCount,
    long MaxReservedByteCount,
    int LatencySensitiveWeight = 3,
    int ThroughputWeight = 1,
    TimeSpan? MaximumThroughputWait = null)
{
    /// <summary>
    /// 返回实际生效的吞吐工作最长等待时间；未显式配置时默认使用两秒。
    /// </summary>
    public TimeSpan EffectiveMaximumThroughputWait =>
        MaximumThroughputWait ?? TimeSpan.FromSeconds(2);

    /// <summary>
    /// 验证当前准入选项的数值范围是否合法。
    /// </summary>
    internal void Validate()
    {
        if (MaxConcurrentOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentOperations));
        }

        if (MaxReservedItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReservedItemCount));
        }

        if (MaxReservedByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReservedByteCount));
        }

        if (LatencySensitiveWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LatencySensitiveWeight));
        }

        if (ThroughputWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ThroughputWeight));
        }

        if (EffectiveMaximumThroughputWait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumThroughputWait));
        }
    }
}

/// <summary>
/// 声明一次独立操作在获准入期间持续占用的资源。
/// </summary>
/// <param name="WorkType">当前操作所属的工作类别。</param>
/// <param name="ReservedItemCount">当前操作预留的项目数量。</param>
/// <param name="ReservedByteCount">当前操作预留的字节数量。</param>
public sealed record ConcurrencyAdmissionRequest(
    ConcurrencyWorkType WorkType,
    int ReservedItemCount = 1,
    long ReservedByteCount = 0)
{
    /// <summary>
    /// 结合当前准入上限验证请求的资源声明是否合法。
    /// </summary>
    /// <param name="options">用于校验请求边界的准入选项。</param>
    internal void Validate(ConcurrencyAdmissionOptions options)
    {
        if (ReservedItemCount <= 0 || ReservedItemCount > options.MaxReservedItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ReservedItemCount));
        }

        if (ReservedByteCount < 0 || ReservedByteCount > options.MaxReservedByteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ReservedByteCount));
        }
    }
}
