namespace NL.Concurrency;

/// <summary>
/// 单个长期 worker 的忙碌/空闲记账快照。
/// </summary>
/// <remarks>
/// <para>
/// 「忙碌」的定义严格限定为**执行工作项**的时间（<c>WorkScheduler.WorkerLoopAsync</c>
/// 中包裹 <c>ticket.Invoke</c> 的区间）。等待唤醒、领取就绪项与结算开销都计入空闲，
/// 因为本报告要回答的是「派给这个 worker 的活有多少」，而不是「线程有多少时间没在睡眠」。
/// </para>
/// <para>
/// <paramref name="Lifetime"/> 从该 worker 进入循环（首次提交时启动）算起，
/// 故未被启动的 worker 三项均为零——不要把它读成「100% 空闲」。
/// </para>
/// </remarks>
/// <param name="WorkerIndex">worker 下标，从 0 连续编号到 <c>WorkerCount - 1</c>。</param>
/// <param name="ExecutedItemCount">该 worker 已执行的工作项数量；失败的工作项也计入（它确实被领走了）。</param>
/// <param name="BusyTime">执行工作项的累计时间。</param>
/// <param name="IdleTime">存活期内未执行工作项的时间；已扣除 <paramref name="BusyTime"/>。</param>
/// <param name="Lifetime">该 worker 从启动到本次快照的存活时间。</param>
public sealed record WorkerUtilization(
    int WorkerIndex,
    long ExecutedItemCount,
    TimeSpan BusyTime,
    TimeSpan IdleTime,
    TimeSpan Lifetime)
{
    /// <summary>
    /// 忙碌时间占存活时间的比例，落在 <c>[0, 1]</c>。
    /// </summary>
    /// <remarks>
    /// 取样时分别读取忙碌与存活，二者之间可能被抢占推进，故这里夹紧上界，
    /// 避免报告出现 &gt; 100% 的使用率。
    /// </remarks>
    public double UtilizationRatio
    {
        get
        {
            if (Lifetime <= TimeSpan.Zero)
            {
                return 0;
            }

            var ratio = BusyTime.TotalSeconds / Lifetime.TotalSeconds;
            return ratio switch
            {
                < 0 => 0,
                > 1 => 1,
                _ => ratio,
            };
        }
    }
}
