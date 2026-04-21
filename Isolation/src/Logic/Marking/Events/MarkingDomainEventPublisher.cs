using Logic.Workflow.Events;

namespace Logic.Marking.Events;

/// <summary>
/// 标记阶段领域事件发布器。
/// </summary>
public sealed class MarkingDomainEventPublisher : IMarkingDomainEventPublisher
{
    private readonly IDomainEventRecorder domainEventRecorder;
    private readonly MarkingEventSequenceBuilder markingEventSequenceBuilder;

    public MarkingDomainEventPublisher(
        IDomainEventRecorder domainEventRecorder,
        MarkingEventSequenceBuilder markingEventSequenceBuilder)
    {
        this.domainEventRecorder = domainEventRecorder;
        this.markingEventSequenceBuilder = markingEventSequenceBuilder;
    }

    public IReadOnlyCollection<DomainEventEnvelope> Publish(MarkingDomainEventPublishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return domainEventRecorder.RecordRange(markingEventSequenceBuilder.BuildEvents(input));
    }
}
