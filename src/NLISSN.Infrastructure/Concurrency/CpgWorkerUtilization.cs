namespace NL.Concurrency;

/// <summary>
/// 单个 CPG WorkBatch worker 的忙碌/空闲记账快照。
/// </summary>
/// <remarks>
/// <para>
/// 「忙碌」严格限定为**执行一个 batch** 的时间（<c>CpgWorkBatchExecutor.WorkerLoopAsync</c>
/// 中从 <c>BatchStarted</c> 到结果写出的区间）。等待 channel 唤醒、等待
/// <c>resultWriter.WriteAsync</c> 背压与领取开销都计入空闲——本报告要回答的是
/// 「派给这个 worker 的活有多少」，而不是「线程有多少时间没在睡眠」。
/// </para>
/// <para>
/// <paramref name="Lifetime"/> 从该 worker 进入循环算起。worker 只在某次阶段执行时启动，
/// 阶段结束后即退出，故它测量的是**该阶段**的存活期，不是整个进程的存活期。
/// </para>
/// </remarks>
/// <param name="WorkerIndex">worker 下标，从 0 连续编号。</param>
/// <param name="ExecutedBatchCount">该 worker 已执行的 batch 数量。</param>
/// <param name="BusyTime">执行 batch 的累计时间。</param>
/// <param name="IdleTime">存活期内未执行 batch 的时间。</param>
/// <param name="Lifetime">该 worker 从启动到本次快照的存活时间。</param>
public sealed record CpgWorkerUtilization(
    int WorkerIndex,
    long ExecutedBatchCount,
    TimeSpan BusyTime,
    TimeSpan IdleTime,
    TimeSpan Lifetime)
{
    /// <summary>
    /// 忙碌时间占存活时间的比例，夹紧到 <c>[0, 1]</c>。
    /// </summary>
    /// <remarks>
    /// 存活期与忙碌时间分别读取，二者之间可能被抢占推进，故夹紧上界，
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
