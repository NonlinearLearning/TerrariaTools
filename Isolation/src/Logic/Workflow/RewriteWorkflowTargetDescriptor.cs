namespace Logic.Workflow;

/// <summary>
/// 表示改写目标阶段关注的目标描述。
/// </summary>
public sealed record RewriteWorkflowTargetDescriptor
{
    public string TargetName { get; init; } = string.Empty;

    public string DocumentPath { get; init; } = string.Empty;

    public string? MemberSignature { get; init; }

    public string? AnchorText { get; init; }
}
