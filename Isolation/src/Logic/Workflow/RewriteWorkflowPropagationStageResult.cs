using Logic.Propagation;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流传播阶段输出。
/// </summary>
public sealed class RewriteWorkflowPropagationStageResult
{
    public PropagationResolution Resolution { get; init; } = null!;
}
