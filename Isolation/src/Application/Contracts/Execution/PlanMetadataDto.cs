using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 计划元数据 DTO。
/// </summary>
public sealed class PlanMetadataDto
{
    public string PlanName { get; set; } = string.Empty;

    public string CompilerVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string? Note { get; set; }
}
