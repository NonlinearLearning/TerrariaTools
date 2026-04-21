using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流计划阶段输出。
/// </summary>
public sealed class RewriteWorkflowPlanStageResult
{
    public RewritePlan Plan { get; init; } = null!;
}
