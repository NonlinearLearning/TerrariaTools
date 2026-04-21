namespace Domain.Decision;

/// <summary>
/// 基于工作流事实推导决策评估。
/// </summary>
public sealed class RewriteDecisionAssessmentPolicy
{
    public RewriteDecisionAssessment Evaluate(RewriteDecisionWorkflowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        bool hasFactReferences = facts.FactReferenceCount > 0;
        bool hasExternalCallers = facts.IncludeExternalReferences && hasFactReferences;

        return new RewriteDecisionAssessment
        {
            ContractExposure = hasExternalCallers
                ? ContractExposure.PublicSurface("propagation-facts")
                : ContractExposure.InternalOnly("local-only"),
            ExternalCallerPresence = hasExternalCallers
                ? ExternalCallerPresence.Detected(facts.ExternalCallers)
                : ExternalCallerPresence.None(),
            ClosureIntegrityAssessment = facts.SimulateFailure
                ? ClosureIntegrityAssessment.Broken("workflow requested simulated failure")
                : ClosureIntegrityAssessment.Verified("workflow propagation remained bounded"),
            RiskScore = facts.SimulateFailure
                ? DecisionRiskScore.High("simulated failure")
                : DecisionRiskScore.Low("bounded propagation"),
        };
    }
}
