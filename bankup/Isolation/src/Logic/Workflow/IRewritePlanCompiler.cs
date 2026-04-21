using Domain.Execution;

namespace Logic.Workflow;

/// <summary>
/// 定义改写计划编译器。
/// </summary>
public interface IRewritePlanCompiler
{
    RewritePlan Compile(RewritePlanCompilationInput input);
}
