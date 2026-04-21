using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 传播轨迹 DTO。
/// </summary>
public sealed class PropagationTraceDto
{
    public string SourceName { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int StepOrder { get; set; }
}
