using Domain.Execution;
using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 表示运行报告装配输入。
/// </summary>
public sealed class RunReportAssemblyInput
{
    public Guid WorkspaceContextId { get; init; }

    public Guid DecisionId { get; init; }

    public Guid PlanId { get; init; }

    public RewriteResult Result { get; init; } = null!;

    public VerificationEvidence Evidence { get; init; } = null!;
}
