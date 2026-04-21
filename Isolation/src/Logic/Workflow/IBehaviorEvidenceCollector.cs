using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 定义行为证据采集器。
/// </summary>
public interface IBehaviorEvidenceCollector
{
    BehaviorEvidence Collect(BehaviorEvidenceCollectionInput input);
}
