using Domain.Decision;

namespace Logic.Decision;

/// <summary>
/// 基于工作流信号构造决策评估。
/// </summary>
public sealed class RewriteDecisionAssessmentBuilder : IRewriteDecisionAssessmentBuilder
{
    private readonly RewriteDecisionAssessmentPolicy rewriteDecisionAssessmentPolicy = new();


    public RewriteDecisionAssessment Build(RewriteDecisionAssessmentBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return rewriteDecisionAssessmentPolicy.Evaluate(new RewriteDecisionWorkflowFacts
        {
            IncludeExternalReferences = input.IncludeExternalReferences,
            FactReferenceCount = input.FactReferenceCount,
            ExternalCallers = input.ExternalCallers,
            SimulateFailure = input.SimulateFailure,
        });
    }
}
