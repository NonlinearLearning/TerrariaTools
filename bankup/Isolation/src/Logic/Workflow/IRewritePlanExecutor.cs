using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 定义改写计划执行器。
/// </summary>
public interface IRewritePlanExecutor
{
    RewriteResult Execute(RewritePlanExecutionInput input);
}
