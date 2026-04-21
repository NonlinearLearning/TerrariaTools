using Domain.Analysis;
using Domain.Analysis.Events;
using Domain.Common.Events;
using Logic.Workflow.Events;

namespace Logic.Analysis.Events;

/// <summary>
/// 负责组装分析阶段领域事件序列。
/// </summary>
public sealed class AnalysisEventSequenceBuilder
{
    public IReadOnlyCollection<IDomainEvent> BuildEvents(AnalysisDomainEventPublishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.WorkspaceContext);

        Guid correlationId = input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContext.Id;
        List<IDomainEvent> events = new();

        if (input.CpgSnapshot is not null)
        {
            CollectAggregateEvents(events, input.CpgSnapshot.DomainEvents, correlationId);
            AppendMissingFallbackEvents(
                events,
                input.CpgSnapshot.Id,
                correlationId,
                string.IsNullOrWhiteSpace(input.EntrySymbol) ? input.CpgSnapshot.EntrySymbol : input.EntrySymbol,
                input.Depth > 0 ? input.Depth : input.CpgSnapshot.Depth,
                input.CpgSnapshot.Nodes.Count + input.CpgSnapshot.Calls.Count + input.CpgSnapshot.Flows.Count);
        }

        if (input.CompositeSnapshot is not null)
        {
            CollectAggregateEvents(events, input.CompositeSnapshot.DomainEvents, correlationId);
            AppendMissingFallbackEvents(
                events,
                input.CompositeSnapshot.Id,
                correlationId,
                input.CompositeSnapshot.CompositionName,
                input.Depth > 0 ? input.Depth : input.CompositeSnapshot.Depth,
                input.CompositeSnapshot.Nodes.Count + input.CompositeSnapshot.LayerNames.Count);
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
            if (events.Any(item =>
                    item.AggregateId == domainEvent.AggregateId &&
                    string.Equals(item.EventName, domainEvent.EventName, StringComparison.Ordinal)))
            {
                continue;
            }

            events.Add(domainEvent);
        }
    }

    private static void AppendMissingFallbackEvents(
        ICollection<IDomainEvent> events,
        Guid snapshotId,
        Guid correlationId,
        string subjectName,
        int depth,
        int factCount)
    {
        if (!events.Any(item => item.AggregateId == snapshotId && item.EventName == "AnalysisSnapshotBuilt"))
        {
            events.Add(new AnalysisSnapshotBuiltDomainEvent(snapshotId, correlationId, subjectName, depth));
        }

        if (!events.Any(item => item.AggregateId == snapshotId && item.EventName == "ProgramFactPublished"))
        {
            events.Add(new ProgramFactPublishedDomainEvent(snapshotId, correlationId, subjectName, factCount));
        }
    }
}
