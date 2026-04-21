using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流执行阶段输出。
/// </summary>
public sealed class RewriteWorkflowExecutionStageResult
{
    public RewritePlan Plan { get; init; } = null!;

    public RewriteResult Result { get; init; } = null!;
}
