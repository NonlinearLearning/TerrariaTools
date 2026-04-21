namespace Logic.Workflow;

/// <summary>
/// 改写工作流产物构造器。
/// </summary>
public sealed class RewriteWorkflowArtifactAssembler : IRewriteWorkflowArtifactAssembler
{
    private readonly IRewriteWorkflowPlanStage rewriteWorkflowPlanStage;
    private readonly IRewriteWorkflowExecutionStage rewriteWorkflowExecutionStage;
    private readonly IRewriteWorkflowEvidenceStage rewriteWorkflowEvidenceStage;
    private readonly IRewriteWorkflowReportStage rewriteWorkflowReportStage;
    private readonly IRewriteWorkflowEventStage rewriteWorkflowEventStage;

    public RewriteWorkflowArtifactAssembler(
        IRewriteWorkflowPlanStage rewriteWorkflowPlanStage,
        IRewriteWorkflowExecutionStage rewriteWorkflowExecutionStage,
        IRewriteWorkflowEvidenceStage rewriteWorkflowEvidenceStage,
        IRewriteWorkflowReportStage rewriteWorkflowReportStage,
        IRewriteWorkflowEventStage rewriteWorkflowEventStage)
    {
        this.rewriteWorkflowPlanStage = rewriteWorkflowPlanStage;
        this.rewriteWorkflowExecutionStage = rewriteWorkflowExecutionStage;
        this.rewriteWorkflowEvidenceStage = rewriteWorkflowEvidenceStage;
        this.rewriteWorkflowReportStage = rewriteWorkflowReportStage;
        this.rewriteWorkflowEventStage = rewriteWorkflowEventStage;
    }


    public RewriteWorkflowArtifacts Assemble(RewriteWorkflowAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RewriteWorkflowPlanStageResult planStage = rewriteWorkflowPlanStage.BuildPlan(input.ToPlanStageInput());
        RewriteWorkflowExecutionStageResult executionStage = rewriteWorkflowExecutionStage.ExecutePlan(input.ToExecutionStageInput(), planStage);
        RewriteWorkflowEvidenceStageResult evidenceStage = rewriteWorkflowEvidenceStage.BuildEvidence(input.ToEvidenceStageInput(), executionStage);
        RewriteWorkflowReportStageResult reportStage = rewriteWorkflowReportStage.BuildReport(input.ToReportStageInput(), evidenceStage);
        return rewriteWorkflowEventStage.RecordEvents(input.ToEventStageInput(), reportStage);
    }
}
