using Domain.Common;
using Domain.Propagation;

namespace Domain.Marking;

/// <summary>
/// 表示可进入传播与决策阶段的变更候选。
/// </summary>
public sealed class ChangeCandidate : AggregateRoot<Guid>
{
    private readonly List<CandidateReason> reasons = new();
    private readonly List<ScenarioTag> scenarioTags = new();
    private readonly List<PropagationTrace> propagationTraces = new();

    private ChangeCandidate(
        Guid id,
        Guid ruleTargetId,
        string ruleCode,
        string targetName,
        CandidateKind candidateKind,
        CandidateReason candidateReason,
        ScenarioTag scenarioTag)
        : base(id)
    {
        RuleTargetId = ruleTargetId;
        RuleCode = ruleCode;
        TargetName = targetName;
        CandidateKind = candidateKind;
        CreatedAt = DateTimeOffset.UtcNow;
        reasons.Add(candidateReason);
        scenarioTags.Add(scenarioTag);
    }

    /// <summary>
    /// 获取规则目标标识。
    /// </summary>
    public Guid RuleTargetId { get; }

    /// <summary>
    /// 获取规则编号。
    /// </summary>
    public string RuleCode { get; }

    /// <summary>
    /// 获取目标名称。
    /// </summary>
    public string TargetName { get; }

    /// <summary>
    /// 获取候选种类。
    /// </summary>
    public CandidateKind CandidateKind { get; }

    /// <summary>
    /// 获取切片边界。
    /// </summary>
    public SliceBoundary? SliceBoundary { get; private set; }

    /// <summary>
    /// 获取是否被父动作覆盖。
    /// </summary>
    public bool IsCoveredByParentAction { get; private set; }

    /// <summary>
    /// 获取覆盖它的父候选标识。
    /// </summary>
    public Guid? CoveredByCandidateId { get; private set; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 获取候选原因集合。
    /// </summary>
    public IReadOnlyCollection<CandidateReason> Reasons => reasons.AsReadOnly();

    /// <summary>
    /// 获取场景标签集合。
    /// </summary>
    public IReadOnlyCollection<ScenarioTag> ScenarioTags => scenarioTags.AsReadOnly();

    /// <summary>
    /// 获取传播轨迹集合。
    /// </summary>
    public IReadOnlyCollection<PropagationTrace> PropagationTraces => propagationTraces.AsReadOnly();

    /// <summary>
    /// 创建变更候选。
    /// </summary>
    public static ChangeCandidate Create(
        Guid ruleTargetId,
        string ruleCode,
        string targetName,
        CandidateKind candidateKind,
        CandidateReason candidateReason,
        ScenarioTag scenarioTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return new ChangeCandidate(
            Guid.NewGuid(),
            ruleTargetId,
            ruleCode.Trim(),
            targetName.Trim(),
            candidateKind,
            candidateReason,
            scenarioTag);
    }

    /// <summary>
    /// 增加候选原因。
    /// </summary>
    public void AddReason(CandidateReason candidateReason)
    {
        if (!reasons.Contains(candidateReason))
        {
            reasons.Add(candidateReason);
        }
    }

    /// <summary>
    /// 增加场景标签。
    /// </summary>
    public void AddScenarioTag(ScenarioTag scenarioTag)
    {
        if (!scenarioTags.Contains(scenarioTag))
        {
            scenarioTags.Add(scenarioTag);
        }
    }

    /// <summary>
    /// 设置切片边界。
    /// </summary>
    public void SetSliceBoundary(SliceBoundary sliceBoundary)
    {
        ArgumentNullException.ThrowIfNull(sliceBoundary);
        SliceBoundary = sliceBoundary;
    }

    /// <summary>
    /// 增加传播轨迹。
    /// </summary>
    public void AddPropagationTrace(PropagationTrace propagationTrace)
    {
        ArgumentNullException.ThrowIfNull(propagationTrace);
        propagationTraces.Add(propagationTrace);
    }

    /// <summary>
    /// 标记被父动作覆盖。
    /// </summary>
    public void MarkCoveredByParentAction(Guid parentCandidateId)
    {
        CoveredByCandidateId = parentCandidateId;
        IsCoveredByParentAction = true;
    }
}

/// <summary>
/// 表示候选种类。
/// </summary>
public enum CandidateKind
{
    Unknown = 0,
    Type = 1,
    Method = 2,
    Member = 3,
    Caller = 4,
    ClosureRoot = 5,
}

/// <summary>
/// 表示场景标签。
/// </summary>
public enum ScenarioTag
{
    Unknown = 0,
    ClassDeletion = 1,
    MethodDeletion = 2,
    MethodPrivatization = 3,
    MethodBodyClearing = 4,
    MemberSlice = 5,
    ShadowClassGeneration = 6,
    MinimalRuntimeClosure = 7,
    PlanDrivenRewrite = 8,
    EvidenceDrivenAudit = 9,
}
