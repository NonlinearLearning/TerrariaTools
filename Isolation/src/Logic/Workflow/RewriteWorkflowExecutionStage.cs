using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 工作流计划执行阶段。
/// </summary>
public sealed class RewriteWorkflowExecutionStage : IRewriteWorkflowExecutionStage
{
    private readonly IRewritePlanExecutor rewritePlanExecutor;

    public RewriteWorkflowExecutionStage(IRewritePlanExecutor rewritePlanExecutor)
    {
        this.rewritePlanExecutor = rewritePlanExecutor;
    }

    public RewriteWorkflowExecutionStageResult ExecutePlan(RewriteWorkflowExecutionStageInput input, RewriteWorkflowPlanStageResult previousStage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(previousStage);

        RewriteResult result = rewritePlanExecutor.Execute(new RewritePlanExecutionInput
        {
            RunCorrelationId = RewriteWorkflowCorrelationResolver.Resolve(input),
            WorkspaceContext = input.WorkspaceContext,
            Plan = previousStage.Plan,
            SourceCode = input.SourceCode,
            ClassName = input.ClassName,
            MethodName = input.MethodName,
            ParameterCount = input.ParameterCount,
        });

        return new RewriteWorkflowExecutionStageResult
        {
            Plan = previousStage.Plan,
            Result = result,
        };
    }
}
