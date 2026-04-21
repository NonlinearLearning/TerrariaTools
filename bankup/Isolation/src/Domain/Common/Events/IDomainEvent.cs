namespace Domain.Common.Events;

/// <summary>
/// 表示领域事件契约。
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    string EventName { get; }

    string ContextName { get; }

    Guid AggregateId { get; }

    Guid CorrelationId { get; }

    Guid? CausationId { get; }

    DateTimeOffset OccurredAt { get; }

    string Summary { get; }
}
