namespace Logic.Workflow;

/// <summary>
/// 定义工作流证据收集阶段能力。
/// </summary>
public interface IRewriteWorkflowEvidenceStage
{
    RewriteWorkflowEvidenceStageResult BuildEvidence(RewriteWorkflowEvidenceStageInput input, RewriteWorkflowExecutionStageResult previousStage);
}
