using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 默认行为证据采集器。
/// </summary>
public sealed class BehaviorEvidenceCollector : IBehaviorEvidenceCollector
{

    public BehaviorEvidence Collect(BehaviorEvidenceCollectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        bool passed = input.RewriteResult.ExecutionFailures.Count == 0;
        string summary = passed ? "行为验证通过。" : "行为验证发现执行失败。";
        return new BehaviorEvidence(input.ScenarioName, passed, summary);
    }
}
