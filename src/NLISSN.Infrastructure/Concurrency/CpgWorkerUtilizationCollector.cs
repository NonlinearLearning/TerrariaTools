namespace NL.Concurrency;

/// <summary>
/// 跨多次 CPG 构建、跨多个 <c>CpgWorkBatchExecutor</c> 实例汇总 per-worker 使用率。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要「跨实例」：NLISSN 对**每个源文件**都新建一个 <c>NLCPGBuilder</c>，
/// 而 worker 池由 builder 持有，故 967 个文件会产生 967 个 worker 池、
/// 每个池里 8 个 worker 各起停一次（每个阶段一次）。单看任一池只能得到
/// 「一个文件的一个阶段」的 utilization，无法回答「8 个并行数到底用满没有」。
/// 故按 <see cref="CpgWorkerUtilization.WorkerIndex"/> 把全部池累加起来。
/// </para>
/// <para>
/// 线程安全：8 个文件并发构建时会有 8 个池同时写入，故全部累加在锁内完成。
/// 写入频率是「每个 worker 每次起停一次 + 每个 batch 一次」，相对 batch 本身的
/// 计算量可以忽略。
/// </para>
/// </remarks>
public sealed class CpgWorkerUtilizationCollector
{
    private readonly object _gate = new();
    private long[] _busyTicks = Array.Empty<long>();
    private long[] _idleTicks = Array.Empty<long>();
    private long[] _lifetimeTicks = Array.Empty<long>();
    private long[] _batchCounts = Array.Empty<long>();
    private int _observedWorkerCount;
    private long _poolExecutionCount;

    /// <summary>已观测到的最大 worker 数量（下标上界 + 1）。</summary>
    public int ObservedWorkerCount
    {
        get
        {
            lock (_gate)
            {
                return _observedWorkerCount;
            }
        }
    }

    /// <summary>
    /// 已累加的执行次数（每次执行 = 一个独立 worker 池）。
    /// </summary>
    /// <remarks>
    /// 报告必须带上这个数：它是「967 个文件 × 若干 pass」把同一批下标累加了多少次，
    /// 缺了它，读者会把累加后的 <c>lifetimeMs</c> 误当成单次池的存活期。
    /// </remarks>
    public long PoolExecutionCount
    {
        get
        {
            lock (_gate)
            {
                return _poolExecutionCount;
            }
        }
    }

    /// <summary>
    /// 登记一次执行所启动的 worker 数量，使从未启动的 worker 也能被报成
    /// <c>never-started</c>，而不是从报告中消失。
    /// </summary>
    public void RecordWorkerCount(int workerCount)
    {
        if (workerCount <= 0)
        {
            return;
        }

        lock (_gate)
        {
            EnsureCapacity(workerCount);
            _poolExecutionCount++;
            if (workerCount > _observedWorkerCount)
            {
                _observedWorkerCount = workerCount;
            }
        }
    }

    /// <summary>
    /// 累加一个 worker 在**一次阶段执行**中的记账结果；<paramref name="utilization"/>
    /// 为 <c>null</c> 表示该 worker 从未启动（记入 <c>never-started</c>）。
    /// </summary>
    public void Record(CpgWorkerUtilization? utilization)
    {
        if (utilization is null)
        {
            return;
        }

        lock (_gate)
        {
            EnsureCapacity(utilization.WorkerIndex + 1);
            _busyTicks[utilization.WorkerIndex] += ToTicks(utilization.BusyTime);
            _idleTicks[utilization.WorkerIndex] += ToTicks(utilization.IdleTime);
            _lifetimeTicks[utilization.WorkerIndex] += ToTicks(utilization.Lifetime);
            _batchCounts[utilization.WorkerIndex] += utilization.ExecutedBatchCount;
            if (utilization.WorkerIndex + 1 > _observedWorkerCount)
            {
                _observedWorkerCount = utilization.WorkerIndex + 1;
            }
        }
    }

    /// <summary>取当前累计快照，下标从 0 连续到 <see cref="ObservedWorkerCount"/> - 1。</summary>
    public IReadOnlyList<CpgWorkerUtilization> Snapshot()
    {
        lock (_gate)
        {
            var result = new CpgWorkerUtilization[_observedWorkerCount];
            for (var index = 0; index < _observedWorkerCount; index++)
            {
                result[index] = new CpgWorkerUtilization(
                    index,
                    _batchCounts[index],
                    FromTicks(_busyTicks[index]),
                    FromTicks(_idleTicks[index]),
                    FromTicks(_lifetimeTicks[index]));
            }

            return result;
        }
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _busyTicks.Length)
        {
            return;
        }

        var capacity = Math.Max(required, Math.Max(4, _busyTicks.Length * 2));
        Array.Resize(ref _busyTicks, capacity);
        Array.Resize(ref _idleTicks, capacity);
        Array.Resize(ref _lifetimeTicks, capacity);
        Array.Resize(ref _batchCounts, capacity);
    }

    private static long ToTicks(TimeSpan value)
    {
        return (long)(value.TotalSeconds * TimeSpan.TicksPerSecond);
    }

    private static TimeSpan FromTicks(long ticks)
    {
        return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(ticks);
    }
}
