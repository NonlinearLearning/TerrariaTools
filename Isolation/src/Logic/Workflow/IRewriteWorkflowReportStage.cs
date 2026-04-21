namespace Logic.Workflow;

/// <summary>
/// 定义工作流报告装配阶段能力。
/// </summary>
public interface IRewriteWorkflowReportStage
{
    RewriteWorkflowReportStageResult BuildReport(RewriteWorkflowReportStageInput input, RewriteWorkflowEvidenceStageResult previousStage);
}
