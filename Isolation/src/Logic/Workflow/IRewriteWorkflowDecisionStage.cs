namespace Logic.Workflow;

/// <summary>
/// 定义工作流决策阶段能力。
/// </summary>
public interface IRewriteWorkflowDecisionStage
{
    RewriteWorkflowDecisionStageResult Decide(RewriteWorkflowDecisionStageInput input);
}
