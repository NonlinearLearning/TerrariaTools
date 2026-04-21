using Domain.Execution;
using Domain.Rewrite.Artifacts;

namespace Logic.Workflow;

/// <summary>
/// 表示改写计划执行输出。
/// </summary>
public sealed class RewritePlanExecutionResult
{
    public RewriteResult Result { get; init; } = null!;

    public CodeRewriteResult? CodeRewriteResult { get; init; }
}
