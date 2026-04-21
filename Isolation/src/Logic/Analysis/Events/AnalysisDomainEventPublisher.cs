using Logic.Workflow.Events;

namespace Logic.Analysis.Events;

/// <summary>
/// 分析阶段领域事件发布器。
/// </summary>
public sealed class AnalysisDomainEventPublisher : IAnalysisDomainEventPublisher
{
    private readonly IDomainEventRecorder domainEventRecorder;
    private readonly AnalysisEventSequenceBuilder analysisEventSequenceBuilder;

    public AnalysisDomainEventPublisher(
        IDomainEventRecorder domainEventRecorder,
        AnalysisEventSequenceBuilder analysisEventSequenceBuilder)
    {
        this.domainEventRecorder = domainEventRecorder;
        this.analysisEventSequenceBuilder = analysisEventSequenceBuilder;
    }

    public IReadOnlyCollection<DomainEventEnvelope> Publish(AnalysisDomainEventPublishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return domainEventRecorder.RecordRange(analysisEventSequenceBuilder.BuildEvents(input));
    }
}
