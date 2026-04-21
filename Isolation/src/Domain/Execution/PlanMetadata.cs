namespace Domain.Execution;

/// <summary>
/// 表示计划元数据。
/// </summary>
public sealed class PlanMetadata
{
    public PlanMetadata(string planName, string compilerVersion, DateTimeOffset createdAt, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planName);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerVersion);
        PlanName = planName.Trim();
        CompilerVersion = compilerVersion.Trim();
        CreatedAt = createdAt;
        Note = note?.Trim();
    }

    public string PlanName { get; }

    public string CompilerVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    public string? Note { get; }
}
