using Domain.Execution;
using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流证据阶段输出。
/// </summary>
public sealed class RewriteWorkflowEvidenceStageResult
{
    public RewritePlan Plan { get; init; } = null!;

    public RewriteResult Result { get; init; } = null!;

    public VerificationEvidence Evidence { get; init; } = null!;
}
