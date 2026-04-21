using Domain.Execution;
using Domain.Rewrite.Artifacts;
using Logic.Rewrite;

namespace Logic.Workflow;

/// <summary>
/// 默认改写计划执行器。
/// </summary>
public sealed class RewritePlanExecutor : IRewritePlanExecutor
{
    private readonly IRoslynCodeIsolationFacade _roslynCodeIsolationFacade;

    public RewritePlanExecutor(IRoslynCodeIsolationFacade roslynCodeIsolationFacade)
    {
        _roslynCodeIsolationFacade = roslynCodeIsolationFacade;
    }


    public RewriteResult Execute(RewritePlanExecutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.WorkspaceContext);
        ArgumentNullException.ThrowIfNull(input.Plan);

        Guid correlationId = input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContext.Id;
        RewriteResult rewriteResult = RewriteResult.Create(input.Plan.Id);

        rewriteResult.StartExecution(correlationId);

        try
        {
            input.Plan.ValidateReadyForExecution();
            PlanChangeItem planItem = input.Plan.ChangeItems.Single();
            ExecuteSingleChange(planItem, input, rewriteResult);
        }
        catch (Exception exception)
        {
            MapFailure(input.Plan, rewriteResult, exception);
        }

        rewriteResult.CompleteExecution(correlationId);

        return rewriteResult;
    }

    private void ExecuteSingleChange(
        PlanChangeItem planChangeItem,
        RewritePlanExecutionInput input,
        RewriteResult rewriteResult)
    {
        ExecutePlanAction(planChangeItem.PlanAction, input);
        rewriteResult.MarkFileChanged(
            planChangeItem.PlanTarget.DocumentPath,
            $"执行动作 {planChangeItem.PlanAction}，目标 {planChangeItem.PlanTarget.TargetName}。",
            [planChangeItem.PlanTarget.TargetName]);
        rewriteResult.AddExecutionTrace(new ExecutionTrace(
            planChangeItem.Id,
            "PlanExecutor",
            $"已执行 {planChangeItem.PlanAction}。",
            DateTimeOffset.UtcNow));
    }

    private static void MapFailure(
        RewritePlan plan,
        RewriteResult rewriteResult,
        Exception exception)
    {
        Guid planChangeItemId = plan.ChangeItems.Select(static item => item.Id).SingleOrDefault();
        rewriteResult.FailExecution(
            planChangeItemId,
            exception.GetType().Name,
            exception.Message,
            false);
    }

    private CodeRewriteResult ExecutePlanAction(PlanAction planAction, RewritePlanExecutionInput input)
    {
        return planAction switch
        {
            PlanAction.DeleteClass => _roslynCodeIsolationFacade.DeleteClass(input.SourceCode, input.ClassName),
            PlanAction.DeleteMethod => _roslynCodeIsolationFacade.DeleteMethod(input.SourceCode, input.ClassName, input.MethodName ?? string.Empty, input.ParameterCount),
            PlanAction.PrivatizeMethod => _roslynCodeIsolationFacade.PrivatizeMethod(input.SourceCode, input.ClassName, input.MethodName ?? string.Empty, input.ParameterCount),
            PlanAction.ClearMethodBody => _roslynCodeIsolationFacade.ClearMethodBody(input.SourceCode, input.ClassName, input.MethodName ?? string.Empty, input.ParameterCount),
            _ => throw new InvalidOperationException($"当前计划动作 {planAction} 尚未接入执行器。"),
        };
    }
}
