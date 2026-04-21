using Domain.Common.Events;
using Domain.Marking.Events;
using Domain.Propagation;
using Domain.Propagation.Events;

namespace Logic.Marking.Events;

/// <summary>
/// 负责组装标记阶段领域事件序列。
/// </summary>
public sealed class MarkingEventSequenceBuilder
{
    public IReadOnlyCollection<IDomainEvent> BuildEvents(MarkingDomainEventPublishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RuleTarget);

        Guid correlationId = input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContextId != Guid.Empty
            ? input.WorkspaceContextId
            : input.RuleTarget.SnapshotId;
        List<IDomainEvent> events = new();
        CollectAggregateEvents(events, input.RuleTarget.DomainEvents, correlationId);
        AppendMissingFallbackEvents(events, new RuleTargetIdentifiedDomainEvent(
            input.RuleTarget.Id,
            correlationId,
            input.RuleTarget.RuleCode.Value,
            input.RuleTarget.Node.DisplayName));

        foreach (ChangeCandidate current in input.Candidates)
        {
            CollectAggregateEvents(events, current.DomainEvents, correlationId);
            AppendMissingFallbackEvents(events, new ChangeCandidateGeneratedDomainEvent(
                current.Id,
                correlationId,
                current.TargetName,
                current.Reasons.Count));
        }

        return events;
    }

    private static void CollectAggregateEvents(
        ICollection<IDomainEvent> events,
        IReadOnlyCollection<IDomainEvent> aggregateEvents,
        Guid correlationId)
    {
        foreach (IDomainEvent domainEvent in aggregateEvents.Where(item => item.CorrelationId == correlationId))
        {
            AppendMissingFallbackEvents(events, domainEvent);
        }
    }

    private static void AppendMissingFallbackEvents(ICollection<IDomainEvent> events, IDomainEvent domainEvent)
    {
        if (events.Any(item =>
                item.AggregateId == domainEvent.AggregateId &&
                string.Equals(item.EventName, domainEvent.EventName, StringComparison.Ordinal)))
        {
            return;
        }

        events.Add(domainEvent);
    }
}
