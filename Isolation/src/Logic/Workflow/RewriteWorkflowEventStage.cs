using Domain.Common.Events;
using Logic.Workflow.Events;

namespace Logic.Workflow;

/// <summary>
/// 工作流事件组装与记录阶段。
/// </summary>
public sealed class RewriteWorkflowEventStage : IRewriteWorkflowEventStage
{
    private readonly IDomainEventRecorder domainEventRecorder;
    private readonly WorkflowEventSequenceBuilder workflowEventSequenceBuilder;

    public RewriteWorkflowEventStage(IDomainEventRecorder domainEventRecorder, WorkflowEventSequenceBuilder workflowEventSequenceBuilder)
    {
        this.domainEventRecorder = domainEventRecorder;
        this.workflowEventSequenceBuilder = workflowEventSequenceBuilder;
    }

    public RewriteWorkflowArtifacts RecordEvents(
        RewriteWorkflowEventStageInput input,
        RewriteWorkflowReportStageResult previousStage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(previousStage);

        RewriteWorkflowArtifacts artifacts = previousStage.ToArtifacts();
        Guid correlationId = RewriteWorkflowCorrelationResolver.Resolve(input);
        IReadOnlyCollection<IDomainEvent> domainEvents = workflowEventSequenceBuilder.BuildEvents(input, artifacts);
        IReadOnlyCollection<IDomainEvent> missingEvents = domainEvents
            .Where(current => !domainEventRecorder.HasRecorded(correlationId, current.EventName, current.AggregateId))
            .ToArray();
        if (missingEvents.Count > 0)
        {
            domainEventRecorder.RecordRange(missingEvents);
        }

        return previousStage.ToArtifacts(domainEventRecorder.GetRecordedEvents(correlationId));
    }
}
