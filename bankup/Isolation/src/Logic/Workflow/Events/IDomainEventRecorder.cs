using Domain.Common.Events;

namespace Logic.Workflow.Events;

/// <summary>
/// 定义领域事件记录器。
/// </summary>
public interface IDomainEventRecorder
{
    void Clear();

    void Clear(Guid correlationId);

    DomainEventEnvelope Record(IDomainEvent domainEvent);

    IReadOnlyCollection<DomainEventEnvelope> RecordRange(IEnumerable<IDomainEvent> domainEvents);

    IReadOnlyCollection<DomainEventEnvelope> GetRecordedEvents();

    IReadOnlyCollection<DomainEventEnvelope> GetRecordedEvents(Guid correlationId);

    bool HasRecorded(Guid correlationId, string eventName, Guid aggregateId);
}
