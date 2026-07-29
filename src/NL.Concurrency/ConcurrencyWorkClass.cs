namespace NL.Concurrency;

/// <summary>
/// 标识独立操作竞争同一本地运行时准入预算时使用的服务类别。
/// </summary>
public enum ConcurrencyWorkClass
{
    /// <summary>
    /// 优先保证较低延迟的工作。
    /// </summary>
    LatencySensitive,

    /// <summary>
    /// 优先保证整体吞吐的工作。
    /// </summary>
    Throughput,
}

/// <summary>
/// 说明一次操作为何获得准入租约。
/// </summary>
public enum ConcurrencyAdmissionReason
{
    /// <summary>
    /// 因加权轮转而获得准入。
    /// </summary>
    WeightedTurn,

    /// <summary>
    /// 因等待时间老化提升而获得准入。
    /// </summary>
    AgingPromotion,
}
