namespace Domain.Execution;

/// <summary>
/// 表示执行失败。
/// </summary>
public sealed class ExecutionFailure
{
    public ExecutionFailure(Guid planChangeItemId, string failureType, string message, bool retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        PlanChangeItemId = planChangeItemId;
        FailureType = failureType.Trim();
        Message = message.Trim();
        Retryable = retryable;
    }

    public Guid PlanChangeItemId { get; }

    public string FailureType { get; }

    public string Message { get; }

    public bool Retryable { get; }
}
