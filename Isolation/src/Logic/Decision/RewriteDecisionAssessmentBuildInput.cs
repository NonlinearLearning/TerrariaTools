namespace Logic.Decision;

/// <summary>
/// 表示决策评估构造输入。
/// </summary>
public sealed class RewriteDecisionAssessmentBuildInput
{
    /// <summary>
    /// 获取或初始化是否包含外部引用。
    /// </summary>
    public bool IncludeExternalReferences { get; init; }

    /// <summary>
    /// 获取或初始化事实引用数量。
    /// </summary>
    public int FactReferenceCount { get; init; }

    /// <summary>
    /// 获取或初始化传播到的外部调用者名称。
    /// </summary>
    public IReadOnlyCollection<string> ExternalCallers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取或初始化是否模拟失败。
    /// </summary>
    public bool SimulateFailure { get; init; }
}
