using Domain.Decision;
using Domain.Rules;

namespace Logic.Decision;

/// <summary>
/// 改写决策构造器。
/// </summary>
public sealed class RewriteDecisionMaker : IRewriteDecisionMaker
{
    private readonly RewriteDecisionResolutionPolicy resolutionPolicy = new();


    public RewriteDecisionResolution Make(RewriteDecisionBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        RewriteDecision decision = RewriteDecision.Create(
            $"{input.TargetName}-decision",
            input.ConfidenceLevel);
        RewriteDecisionOutcome outcome = resolutionPolicy.Resolve(new RewriteDecisionResolutionInput
        {
            CandidateId = input.CandidateId,
            ContractExposure = input.ContractExposure,
            ExternalCallerPresence = input.ExternalCallerPresence,
            ClosureIntegrityAssessment = input.ClosureIntegrityAssessment,
            RiskScore = input.RiskScore,
            ProtectionRules = input.ProtectionRules.Select(RuleCode.Create).ToArray(),
            ConflictTargets = input.ConflictTargets,
            ForceReject = input.ForceReject,
        });
        bool approved = decision.ApplyOutcome(outcome, Guid.Empty);

        return new RewriteDecisionResolution
        {
            CandidateId = input.CandidateId,
            Decision = decision,
            Approved = approved,
        };
    }
}
