using Domain.Common.Events;

namespace Domain.Execution.Events;

/// <summary>
/// 表示执行已完成。
/// </summary>
public sealed class ExecutionCompletedDomainEvent : DomainEventBase
{
    public ExecutionCompletedDomainEvent(
        Guid rewriteResultId,
        Guid correlationId,
        int fileChangeCount,
        int executionFailureCount)
        : base(
            "ExecutionCompleted",
            "RewriteExecution",
            rewriteResultId,
            correlationId,
            null,
            $"执行已完成：文件变更 {fileChangeCount}，失败 {executionFailureCount}。")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileChangeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(executionFailureCount);
        FileChangeCount = fileChangeCount;
        ExecutionFailureCount = executionFailureCount;
    }

    public int FileChangeCount { get; }

    public int ExecutionFailureCount { get; }
}
