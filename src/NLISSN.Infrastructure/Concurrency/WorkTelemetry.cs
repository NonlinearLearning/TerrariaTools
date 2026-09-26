namespace NL.Concurrency;

/// <summary>
/// 一次提交的结算结果，含有序结果与观测到的调度峰值。
/// </summary>
/// <typeparam name="TResult">本次提交产出的事实类型。</typeparam>
/// <param name="Results">按 <see cref="WorkItem{TResult}.StableOrder"/> 升序归并的结果。</param>
/// <param name="PeakReadyCount">峰值同时就绪的工作项数量。</param>
/// <param name="PeakActiveCount">峰值同时在执行的工作项数量。</param>
/// <param name="Elapsed">从提交被接受到结算的总耗时。</param>
public sealed record WorkExecutionOutcome<TResult>(
    IReadOnlyList<TResult> Results,
    int PeakReadyCount,
    int PeakActiveCount,
    TimeSpan Elapsed);

/// <summary>
/// 记录一次提交的调度与资源遥测。取代 ConcurrencyOperationTelemetry，
/// 使运行日志只有一个并发遥测来源。
/// </summary>
/// <param name="Category">提交类别。</param>
/// <param name="InputCount">本次提交的工作项数量。</param>
/// <param name="MaxConcurrency">本次提交实际生效的在途上限。</param>
/// <param name="PeakActiveCount">峰值同时在执行的工作项数量。</param>
/// <param name="PeakReadyCount">峰值同时就绪的工作项数量。</param>
/// <param name="PeakInFlightBytes">峰值在途估算字节数。</param>
/// <param name="MaxQueueWait">单项在就绪队列中的最长等待时间。</param>
/// <param name="Elapsed">从提交到结算的总耗时。</param>
/// <param name="WasCanceled">本次提交是否因取消或失败而提前结束。</param>
/// <param name="RunId">运行标识；未接线时为 null。</param>
public sealed record WorkTelemetry(
    string Category,
    int InputCount,
    int MaxConcurrency,
    int PeakActiveCount,
    int PeakReadyCount,
    long PeakInFlightBytes,
    TimeSpan MaxQueueWait,
    TimeSpan Elapsed,
    bool WasCanceled,
    string? RunId = null);

/// <summary>
/// 定义工作调度遥测记录的接收端。
/// </summary>
public interface IWorkTelemetrySink
{
    /// <summary>记录一次提交遥测。</summary>
    /// <param name="telemetry">要写入的遥测记录。</param>
    void Record(WorkTelemetry telemetry);
}

/// <summary>
/// 在内存中收集工作调度遥测。
/// </summary>
public sealed class WorkTelemetryCollector : IWorkTelemetrySink
{
    private readonly List<WorkTelemetry> _records = [];
    private readonly object _gate = new();

    /// <summary>获取当前已收集的遥测快照。</summary>
    public IReadOnlyList<WorkTelemetry> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    /// <summary>追加一条遥测记录。</summary>
    /// <param name="telemetry">要追加的遥测记录。</param>
    public void Record(WorkTelemetry telemetry)
    {
        lock (_gate)
        {
            _records.Add(telemetry);
        }
    }
}
