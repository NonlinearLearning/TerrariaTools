using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 执行失败 DTO。
/// </summary>
public sealed class ExecutionFailureDto
{
    public Guid PlanChangeItemId { get; set; }

    public string FailureType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool Retryable { get; set; }
}
