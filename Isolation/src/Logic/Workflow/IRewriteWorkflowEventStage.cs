namespace Logic.Workflow;

/// <summary>
/// 定义工作流事件组装与记录阶段能力。
/// </summary>
public interface IRewriteWorkflowEventStage
{
    RewriteWorkflowArtifacts RecordEvents(
        RewriteWorkflowEventStageInput input,
        RewriteWorkflowReportStageResult previousStage);
}
