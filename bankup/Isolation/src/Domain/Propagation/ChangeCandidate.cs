using Domain.Common;
using Domain.Propagation.Events;
using Domain.Rules;

namespace Domain.Propagation;

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
        RuleCode ruleCode,
        TargetName targetName,
        CandidateKind candidateKind,
        CandidateReason candidateReason,
        ScenarioTag scenarioTag)
        : base(id)
    {
        RuleTargetId = ruleTargetId;
        RuleCode = ruleCode;
        TargetNameValue = targetName;
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
    public RuleCode RuleCode { get; }

    /// <summary>
    /// 获取目标名称。
    /// </summary>
    public string TargetName => TargetNameValue.Value;

    /// <summary>
    /// 获取目标名称值对象。
    /// </summary>
    public TargetName TargetNameValue { get; }

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
        RuleCode ruleCode,
        string targetName,
        CandidateKind candidateKind,
        CandidateReason candidateReason,
        ScenarioTag scenarioTag)
    {
        return new ChangeCandidate(
            Guid.NewGuid(),
            ruleTargetId,
            ruleCode,
            Domain.Common.TargetName.Create(targetName),
            candidateKind,
            candidateReason,
            scenarioTag);
    }

    /// <summary>
    /// 基于规则命中目标创建候选。
    /// </summary>
    public static ChangeCandidate CreateFromRuleTarget(
        Marking.RuleTarget ruleTarget,
        CandidateKind candidateKind,
        ScenarioTag scenarioTag)
    {
        ArgumentNullException.ThrowIfNull(ruleTarget);
        return new ChangeCandidate(
            Guid.NewGuid(),
            ruleTarget.Id,
            ruleTarget.RuleCode,
            Domain.Common.TargetName.Create(ruleTarget.Node.DisplayName),
            candidateKind,
            ruleTarget.CandidateReason,
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
        EnsurePropagationMutable();
        if (SliceBoundary is not null && SliceBoundary != sliceBoundary)
        {
            throw new InvalidOperationException("候选的切片边界一旦确定，不允许被不同边界覆盖。");
        }

        SliceBoundary = sliceBoundary;
    }

    /// <summary>
    /// 增加传播轨迹。
    /// </summary>
    public void AddPropagationTrace(PropagationTrace propagationTrace)
    {
        ArgumentNullException.ThrowIfNull(propagationTrace);
        EnsurePropagationMutable();
        bool exists = propagationTraces.Any(current =>
            string.Equals(current.TargetName, propagationTrace.TargetName, StringComparison.Ordinal) &&
            string.Equals(current.Reason, propagationTrace.Reason, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        propagationTraces.Add(propagationTrace);
    }

    /// <summary>
    /// 登记传播目标。
    /// </summary>
    public void RegisterPropagation(TargetName targetName, string reason, int stepOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsurePropagationMutable();
        if (TargetNameValue == targetName)
        {
            return;
        }

        AddPropagationTrace(new PropagationTrace(TargetName, targetName.Value, reason.Trim(), stepOrder));
    }

    public void ApplyPropagation(
        SliceBoundary sliceBoundary,
        IEnumerable<CandidateReason> additionalReasons,
        IEnumerable<ScenarioTag> additionalScenarioTags,
        IEnumerable<TargetName> propagationTargets,
        string propagationReason)
    {
        ArgumentNullException.ThrowIfNull(sliceBoundary);
        ArgumentNullException.ThrowIfNull(additionalReasons);
        ArgumentNullException.ThrowIfNull(additionalScenarioTags);
        ArgumentNullException.ThrowIfNull(propagationTargets);
        ArgumentException.ThrowIfNullOrWhiteSpace(propagationReason);
        EnsurePropagationMutable();

        SetSliceBoundary(sliceBoundary);

        foreach (CandidateReason candidateReason in additionalReasons)
        {
            AddReason(candidateReason);
        }

        foreach (ScenarioTag scenarioTag in additionalScenarioTags)
        {
            AddScenarioTag(scenarioTag);
        }

        int stepOrder = propagationTraces.Count + 1;
        foreach (TargetName targetName in propagationTargets)
        {
            RegisterPropagation(targetName, propagationReason, stepOrder);
            stepOrder++;
        }
    }

    /// <summary>
    /// 判断是否匹配目标名称。
    /// </summary>
    public bool MatchesTarget(string? targetName)
    {
        return TargetNameValue.Matches(targetName);
    }

    /// <summary>
    /// 标记被父动作覆盖。
    /// </summary>
    public void MarkCoveredByParentAction(Guid parentCandidateId)
    {
        MarkCoveredByParentAction(parentCandidateId, Guid.Empty);
    }

    public void MarkCoveredByParentAction(Guid parentCandidateId, Guid correlationId)
    {
        if (parentCandidateId == Guid.Empty)
        {
            throw new InvalidOperationException("父候选标识不能为空。");
        }

        if (parentCandidateId == Id)
        {
            throw new InvalidOperationException("候选不能被自己覆盖。");
        }

        if (IsCoveredByParentAction && CoveredByCandidateId != parentCandidateId)
        {
            throw new InvalidOperationException("候选已被其他父动作覆盖，不能重复改写覆盖关系。");
        }

        CoveredByCandidateId = parentCandidateId;
        IsCoveredByParentAction = true;
        if (correlationId != Guid.Empty && !HasDomainEvent("CandidateCoveredByParentAction", correlationId))
        {
            AddDomainEvent(new CandidateCoveredByParentActionDomainEvent(
                Id,
                correlationId,
                parentCandidateId,
                TargetNameValue,
                TargetNameValue));
        }
    }

    public void ConfirmGenerated(Guid correlationId)
    {
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("ChangeCandidateGenerated", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new ChangeCandidateGeneratedDomainEvent(
            Id,
            resolvedCorrelationId,
            TargetNameValue,
            reasons.Count));
    }

    public void DetectImpactRange(Guid correlationId)
    {
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("ImpactRangeDetected", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new ImpactRangeDetectedDomainEvent(
            Id,
            resolvedCorrelationId,
            TargetNameValue,
            propagationTraces.Count));
    }

    public void DetectRuntimeClosureBoundary(Guid correlationId)
    {
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("RuntimeClosureBoundaryDetected", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new RuntimeClosureBoundaryDetectedDomainEvent(
            Id,
            resolvedCorrelationId,
            TargetNameValue));
    }

    public void DetectShadowBoundary(Guid correlationId)
    {
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("ShadowBoundaryDetected", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new ShadowBoundaryDetectedDomainEvent(
            Id,
            resolvedCorrelationId,
            TargetNameValue));
    }

    public void RegisterLinkedAction(string actionName, string reason, Guid correlationId)
    {
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("LinkedActionDetected", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new LinkedActionDetectedDomainEvent(
            Id,
            resolvedCorrelationId,
            TargetNameValue.Value,
            actionName,
            reason));
    }

    private void EnsurePropagationMutable()
    {
        if (IsCoveredByParentAction)
        {
            throw new InvalidOperationException("已被父动作覆盖的候选不能继续扩张传播边界。");
        }
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
