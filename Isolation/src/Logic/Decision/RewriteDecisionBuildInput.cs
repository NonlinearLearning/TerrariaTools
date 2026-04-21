using Domain.Decision;

namespace Logic.Decision;

/// <summary>
/// 表示改写决策构造输入。
/// </summary>
public sealed class RewriteDecisionBuildInput
{
    /// <summary>
    /// 获取或初始化候选标识。
    /// </summary>
    public Guid CandidateId { get; init; }

    /// <summary>
    /// 获取或初始化目标名称。
    /// </summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化保护规则集合。
    /// </summary>
    public IReadOnlyCollection<string> ProtectionRules { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取或初始化冲突目标集合。
    /// </summary>
    public IReadOnlyCollection<string> ConflictTargets { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取或初始化置信等级。
    /// </summary>
    public ConfidenceLevel ConfidenceLevel { get; init; } = ConfidenceLevel.Medium;

    /// <summary>
    /// 获取或初始化是否强制拒绝。
    /// </summary>
    public bool ForceReject { get; init; }

    /// <summary>
    /// 获取或初始化外部契约暴露情况。
    /// </summary>
    public ContractExposure? ContractExposure { get; init; }

    /// <summary>
    /// 获取或初始化外部调用者存在性。
    /// </summary>
    public ExternalCallerPresence? ExternalCallerPresence { get; init; }

    /// <summary>
    /// 获取或初始化闭包完整性评估。
    /// </summary>
    public ClosureIntegrityAssessment? ClosureIntegrityAssessment { get; init; }

    /// <summary>
    /// 获取或初始化决策风险评分。
    /// </summary>
    public DecisionRiskScore? RiskScore { get; init; }
}
