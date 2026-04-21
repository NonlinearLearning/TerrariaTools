using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 执行轨迹 DTO。
/// </summary>
public sealed class ExecutionTraceDto
{
    public Guid PlanChangeItemId { get; set; }

    public string StepName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }
}
