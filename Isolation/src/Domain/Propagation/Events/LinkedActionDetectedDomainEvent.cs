using Domain.Common.Events;
using Domain.Common;

namespace Domain.Propagation.Events;

/// <summary>
/// 表示传播阶段识别到联动动作。
/// </summary>
public sealed class LinkedActionDetectedDomainEvent : DomainEventBase
{
    public LinkedActionDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        string targetName,
        string linkedActionName,
        string reason)
        : this(candidateId, correlationId, Domain.Common.TargetName.Create(targetName), Domain.Common.TargetName.Create(linkedActionName), reason)
    {
    }

    public LinkedActionDetectedDomainEvent(
        Guid candidateId,
        Guid correlationId,
        TargetName targetName,
        TargetName linkedActionName,
        string reason)
        : base(
            "LinkedActionDetected",
            "ImpactPropagation",
            candidateId,
            correlationId,
            null,
            $"候选 {targetName.Value} 识别到联动动作 {linkedActionName.Value}：{reason}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        TargetNameValue = targetName;
        LinkedActionNameValue = linkedActionName;
        Reason = reason.Trim();
    }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

    public string LinkedActionName => LinkedActionNameValue.Value;

    public TargetName LinkedActionNameValue { get; }

    public string Reason { get; }
}
