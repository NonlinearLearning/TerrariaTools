using System.Diagnostics;

namespace NL.Concurrency;

/// <summary>
/// 单个 CPG WorkBatch worker 的实时忙碌/空闲记账。
/// </summary>
/// <remarks>
/// <para>
/// 记账口径与内核 <c>WorkerAccounting</c> 一致，只把「忙碌」定义为
/// **执行一个 batch**：从 <see cref="BeginBatch"/> 到 <see cref="EndBatch"/>。
/// 等待 channel、等待下游背压、领取开销都算空闲。
/// </para>
/// <para>
/// 计时用 <see cref="Stopwatch.GetTimestamp"/>（单调、无分配），单 worker 单线程访问，
/// 故不需要任何同步；跨 worker 的汇总由 <see cref="CpgWorkerUtilizationCollector"/> 负责。
/// </para>
/// </remarks>
public sealed class CpgWorkerAccounting
{
    private readonly int _workerIndex;
    private readonly long _startTimestamp;
    private long _busyTicks;
    private long _batchCount;
    private long _batchStartTimestamp;

    public CpgWorkerAccounting(int workerIndex)
    {
        _workerIndex = workerIndex;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>该 worker 已执行的 batch 数量。</summary>
    public long ExecutedBatchCount => Interlocked.Read(ref _batchCount);

    /// <summary>标记一个 batch 开始执行。</summary>
    public void BeginBatch()
    {
        _batchStartTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 标记当前 batch 执行结束并计入忙碌时间；未配对的调用被忽略，
    /// 以免取消路径重复调用时把时间重复计入。
    /// </summary>
    /// <remarks>
    /// 计数与计时**分开**处理：即使 <see cref="Stopwatch.GetTimestamp"/> 的分辨率
    /// 让极短 batch 的差值为 0，也仍要计入一次。早期版本在 <c>elapsed &lt;= 0</c> 时
    /// 提前返回，会把"立即返回"的 batch 全部漏掉，使 items 系统性偏小。
    /// </remarks>
    public void EndBatch()
    {
        var started = _batchStartTimestamp;
        if (started == 0)
        {
            return;
        }

        _batchStartTimestamp = 0;
        var elapsed = Stopwatch.GetTimestamp() - started;
        if (elapsed > 0)
        {
            Interlocked.Add(ref _busyTicks, elapsed);
        }

        Interlocked.Increment(ref _batchCount);
    }

    /// <summary>取该 worker 至今的记账快照。</summary>
    /// <remarks>
    /// 存活期按「现在」计算，故同一 worker 在不同时刻取快照会得到不同的
    /// <c>Lifetime</c>——这是有意的：调用方在池收尾后取快照，得到的就是该阶段的完整存活期。
    /// </remarks>
    public CpgWorkerUtilization Snapshot()
    {
        var lifetimeTicks = Stopwatch.GetTimestamp() - _startTimestamp;
        var busyTicks = Interlocked.Read(ref _busyTicks);
        // worker 可能正处在一个尚未 EndBatch 的 batch 中；把这一段也计入，
        // 否则收尾期间取快照会系统性低估忙碌时间。
        var inFlight = _batchStartTimestamp == 0
          ? 0
          : Stopwatch.GetTimestamp() - _batchStartTimestamp;
        if (inFlight > 0)
        {
            busyTicks += inFlight;
        }

        var idleTicks = Math.Max(0, lifetimeTicks - busyTicks);
        return new CpgWorkerUtilization(
          _workerIndex,
          Interlocked.Read(ref _batchCount),
          ToTimeSpan(busyTicks),
          ToTimeSpan(idleTicks),
          ToTimeSpan(lifetimeTicks));
    }

    private static TimeSpan ToTimeSpan(long stopwatchTicks)
    {
        return TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);
    }
}
