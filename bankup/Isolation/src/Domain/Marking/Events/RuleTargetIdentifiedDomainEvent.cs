using Domain.Common.Events;
using Domain.Common;

namespace Domain.Marking.Events;

/// <summary>
/// 表示规则命中对象已识别。
/// </summary>
public sealed class RuleTargetIdentifiedDomainEvent : DomainEventBase
{
    public RuleTargetIdentifiedDomainEvent(
        Guid ruleTargetId,
        Guid correlationId,
        string ruleCode,
        string targetName)
        : this(ruleTargetId, correlationId, ruleCode, Domain.Common.TargetName.Create(targetName))
    {
    }

    public RuleTargetIdentifiedDomainEvent(
        Guid ruleTargetId,
        Guid correlationId,
        string ruleCode,
        TargetName targetName)
        : base(
            "RuleTargetIdentified",
            "RuleScreening",
            ruleTargetId,
            correlationId,
            null,
            $"规则命中已识别：{ruleCode} -> {targetName.Value}。")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);
        RuleCode = ruleCode.Trim();
        TargetNameValue = targetName;
    }

    public string RuleCode { get; }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }
}
