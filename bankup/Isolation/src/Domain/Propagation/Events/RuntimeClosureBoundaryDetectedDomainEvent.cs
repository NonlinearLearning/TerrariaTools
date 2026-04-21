using Domain.Common.Events;
using Domain.Common;

namespace Domain.Propagation.Events;

/// <summary>
/// 表示运行闭包边界已识别。
/// </summary>
public sealed class RuntimeClosureBoundaryDetectedDomainEvent : DomainEventBase
{
    public RuntimeClosureBoundaryDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        string targetName)
        : this(candidateId, correlationId, Domain.Common.TargetName.Create(targetName))
    {
    }

    public RuntimeClosureBoundaryDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        TargetName targetName)
        : base(
            "RuntimeClosureBoundaryDetected",
            "ImpactPropagation",
            candidateId,
            correlationId,
            null,
            $"运行闭包边界已识别：{targetName.Value}。")
    {
        TargetNameValue = targetName;
    }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }
}
