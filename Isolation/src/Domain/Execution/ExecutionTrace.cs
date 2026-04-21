namespace Domain.Execution;

/// <summary>
/// 表示执行轨迹。
/// </summary>
public sealed class ExecutionTrace
{
    public ExecutionTrace(Guid planChangeItemId, string stepName, string message, DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        PlanChangeItemId = planChangeItemId;
        StepName = stepName.Trim();
        Message = message.Trim();
        RecordedAt = recordedAt;
    }

    public Guid PlanChangeItemId { get; }

    public string StepName { get; }

    public string Message { get; }

    public DateTimeOffset RecordedAt { get; }
}
