using Logic.Workflow.Events;

namespace Logic.Propagation.Events;

/// <summary>
/// 传播阶段领域事件发布器。
/// </summary>
public sealed class PropagationDomainEventPublisher : IPropagationDomainEventPublisher
{
    private readonly IDomainEventRecorder domainEventRecorder;
    private readonly PropagationEventSequenceBuilder propagationEventSequenceBuilder;

    public PropagationDomainEventPublisher(
        IDomainEventRecorder domainEventRecorder,
        PropagationEventSequenceBuilder propagationEventSequenceBuilder)
    {
        this.domainEventRecorder = domainEventRecorder;
        this.propagationEventSequenceBuilder = propagationEventSequenceBuilder;
    }

    public IReadOnlyCollection<DomainEventEnvelope> Publish(PropagationDomainEventPublishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return domainEventRecorder.RecordRange(propagationEventSequenceBuilder.BuildEvents(input));
    }
}
