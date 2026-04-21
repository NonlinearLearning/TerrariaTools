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
