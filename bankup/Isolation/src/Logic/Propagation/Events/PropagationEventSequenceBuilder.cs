using Domain.Common.Events;
using Domain.Execution;
using Domain.Propagation.Events;

namespace Logic.Propagation.Events;

/// <summary>
/// 负责组装传播阶段领域事件序列。
/// </summary>
public sealed class PropagationEventSequenceBuilder
{
    public IReadOnlyCollection<IDomainEvent> BuildEvents(PropagationDomainEventPublishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Resolution);
        ArgumentNullException.ThrowIfNull(input.Resolution.Candidate);

        Guid correlationId = input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContextId != Guid.Empty
            ? input.WorkspaceContextId
            : input.Resolution.Candidate.RuleTargetId;
        List<IDomainEvent> events = new();
        CollectAggregateEvents(events, input.Resolution.Candidate.DomainEvents, correlationId);
        int impactedTargetCount = Math.Max(input.Resolution.PropagationTraces.Count, input.Resolution.FactReferences.Count);
        if (impactedTargetCount > 0)
        {
            AppendMissingFallbackEvent(events, new ImpactRangeDetectedDomainEvent(
                input.Resolution.Candidate.Id,
                correlationId,
                input.Resolution.Candidate.TargetName,
                impactedTargetCount));
        }

        if (input.ParentCandidateId.HasValue && !string.IsNullOrWhiteSpace(input.ParentTargetName))
        {
            AppendMissingFallbackEvent(events, new CandidateCoveredByParentActionDomainEvent(
                input.Resolution.Candidate.Id,
                correlationId,
                input.ParentCandidateId.Value,
                input.Resolution.Candidate.TargetName,
                input.ParentTargetName));
        }

        foreach (LinkedActionDescriptor current in input.LinkedActions)
        {
            if (string.IsNullOrWhiteSpace(current.ActionName) || string.IsNullOrWhiteSpace(current.Reason))
            {
                continue;
            }

            AppendMissingFallbackEvent(events, new LinkedActionDetectedDomainEvent(
                input.Resolution.Candidate.Id,
                correlationId,
                input.Resolution.Candidate.TargetName,
                current.ActionName,
                current.Reason));
        }

        if (input.PlanAction == PlanAction.ExtractRuntimeClosure)
        {
            AppendMissingFallbackEvent(events, new RuntimeClosureBoundaryDetectedDomainEvent(
                input.Resolution.Candidate.Id,
                correlationId,
                input.Resolution.Candidate.TargetName));
        }

        if (input.PlanAction == PlanAction.GenerateShadowClass)
        {
            AppendMissingFallbackEvent(events, new ShadowBoundaryDetectedDomainEvent(
                input.Resolution.Candidate.Id,
                correlationId,
                input.Resolution.Candidate.TargetName));
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
            AppendMissingFallbackEvent(events, domainEvent);
        }
    }

    private static void AppendMissingFallbackEvent(ICollection<IDomainEvent> events, IDomainEvent domainEvent)
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
