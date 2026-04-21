namespace Logic.Workflow;

internal static class RewriteWorkflowCorrelationResolver
{
    public static Guid Resolve(RewriteWorkflowAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContextId != Guid.Empty
            ? input.WorkspaceContextId
            : input.WorkspaceContext.Id;
    }

    public static Guid Resolve(RewriteWorkflowPlanStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.RunCorrelationId != Guid.Empty ? input.RunCorrelationId : input.WorkspaceContext.Id;
    }

    public static Guid Resolve(RewriteWorkflowExecutionStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.RunCorrelationId != Guid.Empty ? input.RunCorrelationId : input.WorkspaceContext.Id;
    }

    public static Guid Resolve(RewriteWorkflowEvidenceStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.RunCorrelationId;
    }

    public static Guid Resolve(RewriteWorkflowReportStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.RunCorrelationId;
    }

    public static Guid Resolve(RewriteWorkflowEventStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContextId != Guid.Empty
            ? input.WorkspaceContextId
            : input.WorkspaceContext.Id;
    }
}
