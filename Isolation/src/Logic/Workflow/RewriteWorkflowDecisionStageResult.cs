using Domain.Decision;
using Logic.Decision;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流决策阶段输出。
/// </summary>
public sealed class RewriteWorkflowDecisionStageResult
{
    public RewriteDecisionAssessment Assessment { get; init; } = null!;

    public RewriteDecisionResolution Resolution { get; init; } = null!;
}
