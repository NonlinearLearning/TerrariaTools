using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 工作流计划编译阶段。
/// </summary>
public sealed class RewriteWorkflowPlanStage : IRewriteWorkflowPlanStage
{
    private readonly IRewritePlanCompiler rewritePlanCompiler;

    public RewriteWorkflowPlanStage(IRewritePlanCompiler rewritePlanCompiler)
    {
        this.rewritePlanCompiler = rewritePlanCompiler;
    }

    public RewriteWorkflowPlanStageResult BuildPlan(RewriteWorkflowPlanStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Guid correlationId = RewriteWorkflowCorrelationResolver.Resolve(input);
        input.WorkspaceContext.Prepare(correlationId);

        RewritePlan plan = rewritePlanCompiler.Compile(new RewritePlanCompilationInput
        {
            CandidateId = input.CandidateId,
            Decision = input.Decision,
            TargetName = input.TargetName,
            DocumentPath = input.DocumentPath,
            MemberSignature = input.MemberSignature,
            AnchorText = input.AnchorText,
            PlanAction = input.PlanAction,
        });
        plan.Compile(correlationId);
        return new RewriteWorkflowPlanStageResult
        {
            Plan = plan,
        };
    }
}
