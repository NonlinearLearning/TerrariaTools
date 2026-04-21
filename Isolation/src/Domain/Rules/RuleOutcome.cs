namespace Domain.Rules;

/// <summary>
/// 表示规则执行结果。
/// </summary>
public sealed class RuleOutcome
{
    private readonly List<object> producedArtifacts = new();
    private readonly List<RuleReason> reasons = new();

    private RuleOutcome(
        RuleCode ruleCode,
        RuleOutcomeStatus status,
        string? targetKey,
        RuleOutcomeSeverity severity,
        bool requiresManualReview)
    {
        RuleCode = ruleCode;
        Status = status;
        TargetKey = targetKey?.Trim();
        Severity = severity;
        RequiresManualReview = requiresManualReview;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 获取规则编码。
    /// </summary>
    public RuleCode RuleCode { get; }

    /// <summary>
    /// 获取结果状态。
    /// </summary>
    public RuleOutcomeStatus Status { get; private set; }

    /// <summary>
    /// 获取目标标识。
    /// </summary>
    public string? TargetKey { get; }

    /// <summary>
    /// 获取规则产物集合。
    /// </summary>
    public IReadOnlyCollection<object> ProducedArtifacts => producedArtifacts.AsReadOnly();

    /// <summary>
    /// 获取规则理由集合。
    /// </summary>
    public IReadOnlyCollection<RuleReason> Reasons => reasons.AsReadOnly();

    /// <summary>
    /// 获取严重级别。
    /// </summary>
    public RuleOutcomeSeverity Severity { get; private set; }

    /// <summary>
    /// 获取是否需要人工复核。
    /// </summary>
    public bool RequiresManualReview { get; private set; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 创建规则结果。
    /// </summary>
    public static RuleOutcome Create(
        RuleCode ruleCode,
        RuleOutcomeStatus status,
        string? targetKey = null,
        RuleOutcomeSeverity severity = RuleOutcomeSeverity.Info,
        bool requiresManualReview = false)
    {
        return new RuleOutcome(ruleCode, status, targetKey, severity, requiresManualReview);
    }

    /// <summary>
    /// 增加规则产物。
    /// </summary>
    public void AddArtifact(object artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        producedArtifacts.Add(artifact);
    }

    /// <summary>
    /// 增加规则理由。
    /// </summary>
    public void AddReason(RuleReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        reasons.Add(reason);
    }

    /// <summary>
    /// 更新结果状态。
    /// </summary>
    public void ReviseStatus(RuleOutcomeStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// 更新严重级别。
    /// </summary>
    public void ReviseSeverity(RuleOutcomeSeverity severity)
    {
        Severity = severity;
    }

    /// <summary>
    /// 标记需要人工复核。
    /// </summary>
    public void MarkRequiresManualReview()
    {
        RequiresManualReview = true;
    }

    /// <summary>
    /// 判断是否可进入传播阶段。
    /// </summary>
    public bool CanEnterPropagation()
    {
        return Status is RuleOutcomeStatus.Matched or RuleOutcomeStatus.CandidateProduced;
    }

    /// <summary>
    /// 判断是否可进入决策阶段。
    /// </summary>
    public bool CanEnterDecision()
    {
        return Status is RuleOutcomeStatus.CandidateProduced or RuleOutcomeStatus.ProtectionApplied or RuleOutcomeStatus.Rejected;
    }

    /// <summary>
    /// 判断是否可进入计划阶段。
    /// </summary>
    public bool CanEnterPlanning()
    {
        return Status == RuleOutcomeStatus.CandidateProduced;
    }
}

/// <summary>
/// 表示规则结果状态。
/// </summary>
public enum RuleOutcomeStatus
{
    Unknown = 0,
    Matched = 1,
    Skipped = 2,
    CandidateProduced = 3,
    ProtectionApplied = 4,
    Rejected = 5,
    EvidenceOnly = 6,
    Blocked = 7,
    Failed = 8,
}

/// <summary>
/// 表示规则结果严重级别。
/// </summary>
public enum RuleOutcomeSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}
