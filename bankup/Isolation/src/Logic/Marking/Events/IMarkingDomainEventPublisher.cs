using Logic.Workflow.Events;

namespace Logic.Marking.Events;

/// <summary>
/// 定义标记阶段领域事件发布能力。
/// </summary>
public interface IMarkingDomainEventPublisher
{
    IReadOnlyCollection<DomainEventEnvelope> Publish(MarkingDomainEventPublishInput input);
}
