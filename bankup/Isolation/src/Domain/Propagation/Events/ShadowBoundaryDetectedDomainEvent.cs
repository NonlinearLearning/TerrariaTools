using Domain.Common.Events;
using Domain.Common;

namespace Domain.Propagation.Events;

/// <summary>
/// 表示影子边界已识别。
/// </summary>
public sealed class ShadowBoundaryDetectedDomainEvent : DomainEventBase
{
    public ShadowBoundaryDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        string targetName)
        : this(candidateId, correlationId, Domain.Common.TargetName.Create(targetName))
    {
    }

    public ShadowBoundaryDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        TargetName targetName)
        : base(
            "ShadowBoundaryDetected",
            "ImpactPropagation",
            candidateId,
            correlationId,
            null,
            $"影子边界已识别：{targetName.Value}。")
    {
        TargetNameValue = targetName;
    }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }
}
