using Domain.Common.Events;
using Domain.Common;

namespace Domain.Propagation.Events;

/// <summary>
/// 表示变更候选已生成。
/// </summary>
public sealed class ChangeCandidateGeneratedDomainEvent : DomainEventBase
{
    public ChangeCandidateGeneratedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        string targetName,
        int reasonCount)
        : this(candidateId, correlationId, Domain.Common.TargetName.Create(targetName), reasonCount)
    {
    }

    public ChangeCandidateGeneratedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        TargetName targetName,
        int reasonCount)
        : base(
            "ChangeCandidateGenerated",
            "ImpactPropagation",
            candidateId,
            correlationId,
            null,
            $"变更候选已生成：{targetName.Value}，原因数 {reasonCount}。")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reasonCount);
        TargetNameValue = targetName;
        ReasonCount = reasonCount;
    }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

    public int ReasonCount { get; }
}
