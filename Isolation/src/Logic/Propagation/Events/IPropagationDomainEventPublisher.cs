using Logic.Workflow.Events;

namespace Logic.Propagation.Events;

/// <summary>
/// 定义传播阶段领域事件发布能力。
/// </summary>
public interface IPropagationDomainEventPublisher
{
    IReadOnlyCollection<DomainEventEnvelope> Publish(PropagationDomainEventPublishInput input);
}
