using System.Collections.Concurrent;

namespace NL.Concurrency;

/// <summary>
/// 标识并发池执行的操作类别。
/// </summary>
public enum ConcurrencyOperationKind
{
    /// <summary>
    /// 有序选择。
    /// </summary>
    OrderedSelection,

    /// <summary>
    /// 处理器密集型有序选择。
    /// </summary>
    CpuBoundOrderedSelection,

    /// <summary>
    /// 有序提交。
    /// </summary>
    OrderedCommit,

    /// <summary>
    /// 两阶段有序提交。
    /// </summary>
    TwoStageOrderedCommit,

    /// <summary>
    /// 并发逐项处理。
    /// </summary>
    ForEach,

    /// <summary>
    /// 依赖图调度。
    /// </summary>
    DependencyGraph,

    /// <summary>
    /// 固定长期 worker 执行。
    /// </summary>
    FixedWorkers,
}

/// <summary>
/// 记录一次并发池操作的调度与资源遥测。
/// </summary>
/// <param name="OperationKind">操作类别。</param>
/// <param name="SourceCount">输入项数量。</param>
/// <param name="RequestedMaxDegreeOfParallelism">请求的最大并发度。</param>
/// <param name="PeakActiveWorkItemCount">峰值活动工作项数量。</param>
/// <param name="PeakReadyWorkItemCount">峰值就绪工作项数量。</param>
/// <param name="PeakCompletedBufferItemCount">峰值已完成缓冲项数量。</param>
/// <param name="PeakRetainedRecordCount">峰值保留记录数量。</param>
/// <param name="WorkType">工作类别。</param>
/// <param name="PeakReservedByteCount">峰值预留字节数。</param>
/// <param name="QueueWait">排队等待时间。</param>
/// <param name="AdmissionReason">获得准入的原因。</param>
/// <param name="Elapsed">总耗时。</param>
/// <param name="WasCanceled">是否因取消而结束。</param>
/// <param name="FailureDrainElapsed">失败后排空剩余工作的耗时。</param>
public sealed record ConcurrencyOperationTelemetry(
  ConcurrencyOperationKind OperationKind,
  int SourceCount,
  int RequestedMaxDegreeOfParallelism,
  int PeakActiveWorkItemCount,
  int PeakReadyWorkItemCount,
  int PeakCompletedBufferItemCount,
  int PeakRetainedRecordCount,
  ConcurrencyWorkType WorkType,
  long PeakReservedByteCount,
  TimeSpan QueueWait,
  ConcurrencyAdmissionReason? AdmissionReason,
  TimeSpan Elapsed,
  bool WasCanceled,
  TimeSpan FailureDrainElapsed);

/// <summary>
/// 定义并发池遥测记录的接收端。
/// </summary>
public interface IConcurrencyPoolTelemetrySink
{
    /// <summary>
    /// 记录一次并发池操作遥测。
    /// </summary>
    /// <param name="operation">要写入的操作遥测。</param>
    void Record(ConcurrencyOperationTelemetry operation);
}

/// <summary>
/// 在内存中收集并发池操作遥测。
/// </summary>
public sealed class ConcurrencyOperationTelemetryCollector : IConcurrencyPoolTelemetrySink
{
    private readonly ConcurrentQueue<ConcurrencyOperationTelemetry> _operations = new();

    /// <summary>
    /// 获取当前已收集的操作遥测快照。
    /// </summary>
    public IReadOnlyList<ConcurrencyOperationTelemetry> Operations => _operations.ToArray();

    /// <summary>
    /// 追加一条操作遥测记录。
    /// </summary>
    /// <param name="operation">要追加的操作遥测。</param>
    public void Record(ConcurrencyOperationTelemetry operation)
    {
        _operations.Enqueue(operation);
    }
}
