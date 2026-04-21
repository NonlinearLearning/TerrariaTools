using Domain.Common.Events;

namespace Logic.Workflow.Events;

/// <summary>
/// 基于内存的领域事件记录器。
/// </summary>
public sealed class InMemoryDomainEventRecorder : IDomainEventRecorder
{
    private readonly List<DomainEventEnvelope> recordedEvents = new();

    public void Clear()
    {
        recordedEvents.Clear();
    }

    public void Clear(Guid correlationId)
    {
        recordedEvents.RemoveAll(item => item.DomainEvent.CorrelationId == correlationId);
    }

    public DomainEventEnvelope Record(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        DomainEventEnvelope envelope = new(recordedEvents.Count + 1, domainEvent);
        recordedEvents.Add(envelope);
        return envelope;
    }

    public IReadOnlyCollection<DomainEventEnvelope> RecordRange(IEnumerable<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        List<DomainEventEnvelope> envelopes = new();

        foreach (IDomainEvent current in domainEvents)
        {
            envelopes.Add(Record(current));
        }

        return envelopes;
    }

    public IReadOnlyCollection<DomainEventEnvelope> GetRecordedEvents()
    {
        return recordedEvents.AsReadOnly();
    }

    public IReadOnlyCollection<DomainEventEnvelope> GetRecordedEvents(Guid correlationId)
    {
        return recordedEvents
            .Where(item => item.DomainEvent.CorrelationId == correlationId)
            .OrderBy(item => item.Sequence)
            .ToArray();
    }

    public bool HasRecorded(Guid correlationId, string eventName, Guid aggregateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        return recordedEvents.Any(item =>
            item.DomainEvent.CorrelationId == correlationId &&
            item.DomainEvent.AggregateId == aggregateId &&
            string.Equals(item.DomainEvent.EventName, eventName, StringComparison.Ordinal));
    }
}
