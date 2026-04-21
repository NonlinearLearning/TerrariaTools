using Application.Contracts.Workflow;
using Logic.Workflow.Events;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static WorkflowDomainEventDto Map(DomainEventEnvelope envelope)
    {
        return new WorkflowDomainEventDto
        {
            Sequence = envelope.Sequence,
            EventId = envelope.DomainEvent.EventId,
            EventName = envelope.DomainEvent.EventName,
            ContextName = envelope.DomainEvent.ContextName,
            AggregateId = envelope.DomainEvent.AggregateId,
            CorrelationId = envelope.DomainEvent.CorrelationId,
            CausationId = envelope.DomainEvent.CausationId,
            OccurredAt = envelope.DomainEvent.OccurredAt,
            Summary = envelope.DomainEvent.Summary,
        };
    }

    public static IReadOnlyCollection<WorkflowDomainEventDto> Map(IReadOnlyCollection<DomainEventEnvelope> envelopes)
    {
        return envelopes.Select(Map).ToArray();
    }
}
