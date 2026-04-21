namespace Logic.Workflow;

/// <summary>
/// 表示改写执行阶段关注的源码定位描述。
/// </summary>
public sealed record RewriteWorkflowExecutionDescriptor
{
    public string SourceCode { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public string? MethodName { get; init; }

    public int? ParameterCount { get; init; }
}
