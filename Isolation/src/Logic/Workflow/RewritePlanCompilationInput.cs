using Domain.Decision;
using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 表示改写计划编译输入。
/// </summary>
public sealed class RewritePlanCompilationInput
{
    public Guid CandidateId { get; init; }

    public RewriteDecision Decision { get; init; } = null!;

    public string TargetName { get; init; } = string.Empty;

    public string DocumentPath { get; init; } = string.Empty;

    public string? MemberSignature { get; init; }

    public string? AnchorText { get; init; }

    public PlanAction PlanAction { get; init; }
}
