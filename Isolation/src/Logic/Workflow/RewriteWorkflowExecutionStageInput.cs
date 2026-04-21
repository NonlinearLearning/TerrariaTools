using Domain.Workspaces;

namespace Logic.Workflow;

public sealed class RewriteWorkflowExecutionStageInput
{
    public Guid RunCorrelationId { get; init; }
    public WorkspaceContext WorkspaceContext { get; init; } = null!;
    public string SourceCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string? MethodName { get; init; }
    public int? ParameterCount { get; init; }
}
