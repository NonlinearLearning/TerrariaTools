using Domain.Common.Events;
using Domain.Common;

namespace Domain.Propagation.Events;

/// <summary>
/// 表示候选已被父动作覆盖。
/// </summary>
public sealed class CandidateCoveredByParentActionDomainEvent : DomainEventBase
{
    public CandidateCoveredByParentActionDomainEvent(
        Guid candidateId,
        Guid correlationId,
        Guid parentCandidateId,
        string targetName,
        string parentTargetName)
        : this(candidateId, correlationId, parentCandidateId, Domain.Common.TargetName.Create(targetName), Domain.Common.TargetName.Create(parentTargetName))
    {
    }

    public CandidateCoveredByParentActionDomainEvent(
        Guid candidateId,
        Guid correlationId,
        Guid parentCandidateId,
        TargetName targetName,
        TargetName parentTargetName)
        : base(
            "CandidateCoveredByParentAction",
            "ImpactPropagation",
            candidateId,
            correlationId,
            null,
            $"候选 {targetName.Value} 已被父动作 {parentTargetName.Value} 覆盖。")
    {
        ParentCandidateId = parentCandidateId;
        TargetNameValue = targetName;
        ParentTargetNameValue = parentTargetName;
    }

    public Guid ParentCandidateId { get; }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

    public string ParentTargetName => ParentTargetNameValue.Value;

    public TargetName ParentTargetNameValue { get; }
}
