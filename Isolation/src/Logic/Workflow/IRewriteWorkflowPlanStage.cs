using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 定义工作流计划编译阶段能力。
/// </summary>
public interface IRewriteWorkflowPlanStage
{
    RewriteWorkflowPlanStageResult BuildPlan(RewriteWorkflowPlanStageInput input);
}
