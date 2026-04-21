using Domain.Execution;
using Domain.Output.Audit;
using Domain.Output.Verification;
using Logic.Workflow.Events;

namespace Logic.Workflow;

/// <summary>
/// 表示改写工作流构造产物。
/// </summary>
public sealed class RewriteWorkflowArtifacts
{
    /// <summary>
    /// 获取或初始化改写计划。
    /// </summary>
    public RewritePlan Plan { get; init; } = null!;

    /// <summary>
    /// 获取或初始化改写结果。
    /// </summary>
    public RewriteResult Result { get; init; } = null!;

    /// <summary>
    /// 获取或初始化验证证据。
    /// </summary>
    public VerificationEvidence Evidence { get; init; } = null!;

    /// <summary>
    /// 获取或初始化运行报告。
    /// </summary>
    public RunReport Report { get; init; } = null!;

    /// <summary>
    /// 获取或初始化领域事件集合。
    /// </summary>
    public IReadOnlyCollection<DomainEventEnvelope> DomainEvents { get; init; } = Array.Empty<DomainEventEnvelope>();
}
