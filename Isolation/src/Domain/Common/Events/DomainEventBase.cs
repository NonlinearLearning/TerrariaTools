namespace Domain.Common.Events;

/// <summary>
/// 表示领域事件基类。
/// </summary>
public abstract class DomainEventBase : IDomainEvent
{
    protected DomainEventBase(
        string eventName,
        string contextName,
        Guid aggregateId,
        Guid correlationId,
        Guid? causationId,
        string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextName);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (aggregateId == Guid.Empty)
        {
            throw new InvalidOperationException("领域事件聚合标识不能为空。");
        }

        if (correlationId == Guid.Empty)
        {
            throw new InvalidOperationException("领域事件关联标识不能为空。");
        }

        EventId = Guid.NewGuid();
        EventName = eventName.Trim();
        ContextName = contextName.Trim();
        AggregateId = aggregateId;
        CorrelationId = correlationId;
        CausationId = causationId;
        OccurredAt = DateTimeOffset.UtcNow;
        Summary = summary.Trim();
    }

    public Guid EventId { get; }

    public string EventName { get; }

    public string ContextName { get; }

    public Guid AggregateId { get; }

    public Guid CorrelationId { get; }

    public Guid? CausationId { get; }

    public DateTimeOffset OccurredAt { get; }

    public string Summary { get; }
}
