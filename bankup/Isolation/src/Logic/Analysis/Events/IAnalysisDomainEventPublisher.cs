using Logic.Workflow.Events;

namespace Logic.Analysis.Events;

/// <summary>
/// 定义分析阶段领域事件发布能力。
/// </summary>
public interface IAnalysisDomainEventPublisher
{
    IReadOnlyCollection<DomainEventEnvelope> Publish(AnalysisDomainEventPublishInput input);
}
