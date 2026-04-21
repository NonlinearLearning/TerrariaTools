namespace Logic.Workflow;

/// <summary>
/// 定义工作流计划执行阶段能力。
/// </summary>
public interface IRewriteWorkflowExecutionStage
{
    RewriteWorkflowExecutionStageResult ExecutePlan(RewriteWorkflowExecutionStageInput input, RewriteWorkflowPlanStageResult previousStage);
}
