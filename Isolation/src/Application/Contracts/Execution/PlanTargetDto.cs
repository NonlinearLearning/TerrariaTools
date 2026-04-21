using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 计划目标 DTO。
/// </summary>
public sealed class PlanTargetDto
{
    public string DocumentPath { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public string? MemberSignature { get; set; }

    public string? AnchorText { get; set; }
}
