using Domain.Execution;
using Domain.Output.Audit;
using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 工作流报告装配阶段。
/// </summary>
public sealed class RewriteWorkflowReportStage : IRewriteWorkflowReportStage
{
    private readonly IRunReportAssembler runReportAssembler;

    public RewriteWorkflowReportStage(IRunReportAssembler runReportAssembler)
    {
        this.runReportAssembler = runReportAssembler;
    }

    public RewriteWorkflowReportStageResult BuildReport(RewriteWorkflowReportStageInput input, RewriteWorkflowEvidenceStageResult previousStage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(previousStage);

        RewritePlan plan = previousStage.Plan;
        RewriteResult result = previousStage.Result;
        VerificationEvidence evidence = previousStage.Evidence;
        RunReport report = runReportAssembler.Assemble(new RunReportAssemblyInput
        {
            WorkspaceContextId = input.WorkspaceContextId,
            DecisionId = input.DecisionId == Guid.Empty ? input.Decision.Id : input.DecisionId,
            PlanId = plan.Id,
            Result = result,
            Evidence = evidence,
        });
        report.Finalize(RewriteWorkflowCorrelationResolver.Resolve(input));
        return new RewriteWorkflowReportStageResult
        {
            Plan = plan,
            Result = result,
            Evidence = evidence,
            Report = report,
        };
    }
}
