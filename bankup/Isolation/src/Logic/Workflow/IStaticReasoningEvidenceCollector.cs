using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 定义静态推理证据采集器。
/// </summary>
public interface IStaticReasoningEvidenceCollector
{
    StaticReasoningEvidence Collect(StaticReasoningEvidenceCollectionInput input);
}
