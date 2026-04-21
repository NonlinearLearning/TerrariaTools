namespace Domain.Rules;

/// <summary>
/// 表示规则执行策略。
/// </summary>
public sealed class RuleExecutionPolicy : Domain.Common.ISharedKernelType
{
    /// <summary>
    /// 初始化规则执行策略。
    /// </summary>
    public RuleExecutionPolicy(
        RuleParticipationMode participationMode,
        RuleConflictMode conflictMode,
        RuleFailureMode failureMode,
        RuleSafetyLevel safetyLevel,
        RuleEvidenceMode evidenceMode)
    {
        ParticipationMode = participationMode;
        ConflictMode = conflictMode;
        FailureMode = failureMode;
        SafetyLevel = safetyLevel;
        EvidenceMode = evidenceMode;
    }

    /// <summary>
    /// 获取参与模式。
    /// </summary>
    public RuleParticipationMode ParticipationMode { get; }

    /// <summary>
    /// 获取冲突模式。
    /// </summary>
    public RuleConflictMode ConflictMode { get; }

    /// <summary>
    /// 获取失败模式。
    /// </summary>
    public RuleFailureMode FailureMode { get; }

    /// <summary>
    /// 获取安全等级。
    /// </summary>
    public RuleSafetyLevel SafetyLevel { get; }

    /// <summary>
    /// 获取证据模式。
    /// </summary>
    public RuleEvidenceMode EvidenceMode { get; }

    /// <summary>
    /// 判断是否可产生候选。
    /// </summary>
    public bool CanProduceCandidate()
    {
        return ParticipationMode == RuleParticipationMode.Candidate ||
               ParticipationMode == RuleParticipationMode.Decision;
    }

    /// <summary>
    /// 判断是否可阻断流程。
    /// </summary>
    public bool CanBlockWorkflow()
    {
        return FailureMode == RuleFailureMode.BlockWorkflow;
    }

    /// <summary>
    /// 判断是否仅参与证据。
    /// </summary>
    public bool IsEvidenceOnly()
    {
        return ParticipationMode == RuleParticipationMode.EvidenceOnly;
    }
}

/// <summary>
/// 表示规则参与模式。
/// </summary>
public enum RuleParticipationMode
{
    Unknown = 0,
    MarkOnly = 1,
    Candidate = 2,
    Protection = 3,
    Decision = 4,
    EvidenceOnly = 5,
}

/// <summary>
/// 表示规则冲突模式。
/// </summary>
public enum RuleConflictMode
{
    Unknown = 0,
    PreferHigherPriority = 1,
    BlockOnConflict = 2,
    MergeReasons = 3,
    KeepAllForReview = 4,
}

/// <summary>
/// 表示规则失败模式。
/// </summary>
public enum RuleFailureMode
{
    Unknown = 0,
    Skip = 1,
    Warn = 2,
    BlockWorkflow = 3,
}

/// <summary>
/// 表示规则安全等级。
/// </summary>
public enum RuleSafetyLevel
{
    Unknown = 0,
    Conservative = 1,
    Balanced = 2,
    Aggressive = 3,
}

/// <summary>
/// 表示规则证据模式。
/// </summary>
public enum RuleEvidenceMode
{
    None = 0,
    AttachReason = 1,
    AttachTrace = 2,
    AttachRisk = 3,
}
