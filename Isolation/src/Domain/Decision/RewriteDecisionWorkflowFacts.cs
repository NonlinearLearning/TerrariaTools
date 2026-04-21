namespace Domain.Decision;

/// <summary>
/// 表示工作流阶段提供给决策域的事实输入。
/// </summary>
public sealed class RewriteDecisionWorkflowFacts
{
    public bool IncludeExternalReferences { get; init; }

    public int FactReferenceCount { get; init; }

    public IReadOnlyCollection<string> ExternalCallers { get; init; } = Array.Empty<string>();

    public bool SimulateFailure { get; init; }
}
