namespace Logic.Workflow;

/// <summary>
/// 定义工作流传播阶段能力。
/// </summary>
public interface IRewriteWorkflowPropagationStage
{
    RewriteWorkflowPropagationStageResult Propagate(RewriteWorkflowPropagationStageInput input);
}
