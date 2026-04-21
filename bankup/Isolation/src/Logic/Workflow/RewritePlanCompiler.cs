using Domain.Decision;
using Domain.Execution;
using Domain.Workspaces;

namespace Logic.Workflow;

/// <summary>
/// 默认改写计划编译器。
/// </summary>
public sealed class RewritePlanCompiler : IRewritePlanCompiler
{

    public RewritePlan Compile(RewritePlanCompilationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Decision);

        RewritePlan plan = RewritePlan.Create(new PlanMetadata(
            $"{input.TargetName}-plan",
            "rewrite-plan-compiler/1.0.0",
            DateTimeOffset.UtcNow,
            "由计划编译器生成。"));

        plan.ApplyDecisionOutcome(
            input.CandidateId,
            input.Decision,
            BuildPlanTarget(input),
            input.PlanAction);

        return plan;
    }

    private static PlanTarget BuildPlanTarget(RewritePlanCompilationInput input)
    {
        return new PlanTarget(
            DocumentPath.Create(input.DocumentPath),
            input.TargetName,
            input.MemberSignature,
            input.AnchorText);
    }
}
