using Domain.Common.Events;
using Domain.Common;

namespace Domain.Propagation.Events;

/// <summary>
/// 表示影响范围已识别。
/// </summary>
public sealed class ImpactRangeDetectedDomainEvent : DomainEventBase
{
    public ImpactRangeDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        string targetName,
        int impactedTargetCount)
        : this(candidateId, correlationId, Domain.Common.TargetName.Create(targetName), impactedTargetCount)
    {
    }

    public ImpactRangeDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        TargetName targetName,
        int impactedTargetCount)
        : base(
            "ImpactRangeDetected",
            "ImpactPropagation",
            candidateId,
            correlationId,
            null,
            $"影响范围已识别：{targetName.Value}，波及目标 {impactedTargetCount} 个。")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(impactedTargetCount);
        TargetNameValue = targetName;
        ImpactedTargetCount = impactedTargetCount;
    }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

    public int ImpactedTargetCount { get; }
}
