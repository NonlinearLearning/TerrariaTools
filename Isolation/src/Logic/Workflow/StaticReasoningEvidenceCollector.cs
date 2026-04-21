using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 默认静态推理证据采集器。
/// </summary>
public sealed class StaticReasoningEvidenceCollector : IStaticReasoningEvidenceCollector
{

    public StaticReasoningEvidence Collect(StaticReasoningEvidenceCollectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string summary = input.ReasonChain.Count == 0
            ? "未提供推理链。"
            : string.Join(" -> ", input.ReasonChain);
        return new StaticReasoningEvidence(input.SubjectName, summary);
    }
}
