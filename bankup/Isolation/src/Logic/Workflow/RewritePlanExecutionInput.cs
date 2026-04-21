using Domain.Execution;
using Domain.Workspaces;

namespace Logic.Workflow;

/// <summary>
/// 表示改写计划执行输入。
/// </summary>
public sealed class RewritePlanExecutionInput
{
    public Guid RunCorrelationId { get; init; }

    public WorkspaceContext WorkspaceContext { get; init; } = null!;

    public RewritePlan Plan { get; init; } = null!;

    public string SourceCode { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public string? MethodName { get; init; }

    public int? ParameterCount { get; init; }
}
