using Domain.Rules;

namespace Domain.Decision;

/// <summary>
/// 表示改写决策领域解释策略。
/// </summary>
public sealed class RewriteDecisionResolutionPolicy
{
    public RewriteDecisionOutcome Resolve(RewriteDecisionResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<DecisionProtection> protections = BuildProtections(input.CandidateId, input.ProtectionRules);
        List<DecisionConflict> conflicts = BuildConflicts(input.CandidateId, input.ConflictTargets);
        RejectionReason? rejectionReason = ResolveRejectionReason(input, protections);
        return new RewriteDecisionOutcome(
            input.CandidateId,
            ApprovalReason.PropagationBounded,
            rejectionReason,
            protections,
            conflicts);
    }

    private static List<DecisionProtection> BuildProtections(Guid candidateId, IReadOnlyCollection<RuleCode>? protectionRules)
    {
        List<DecisionProtection> protections = new();
        foreach (RuleCode ruleCode in protectionRules ?? Array.Empty<RuleCode>())
        {
            protections.Add(new DecisionProtection(
                candidateId,
                ruleCode,
                $"保护规则 {ruleCode.Value} 阻止直接激进改写。"));
        }

        return protections;
    }

    private static List<DecisionConflict> BuildConflicts(Guid candidateId, IReadOnlyCollection<string>? conflictTargets)
    {
        List<DecisionConflict> conflicts = new();
        foreach (string conflictTarget in conflictTargets ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(conflictTarget))
            {
                continue;
            }

            conflicts.Add(new DecisionConflict(
                candidateId,
                Guid.NewGuid(),
                $"与 {conflictTarget.Trim()} 存在计划冲突。"));
        }

        return conflicts;
    }

    private static RejectionReason? ResolveRejectionReason(
        RewriteDecisionResolutionInput input,
        IReadOnlyCollection<DecisionProtection> protections)
    {
        if (input.ContractExposure?.IsPublicSurface == true)
        {
            return RejectionReason.ExternalContractDetected;
        }

        if (input.ExternalCallerPresence?.Exists == true)
        {
            return RejectionReason.ExternalCallerDetected;
        }

        if (input.ClosureIntegrityAssessment?.IsBroken == true)
        {
            return RejectionReason.ClosureIntegrityBroken;
        }

        if (input.RiskScore?.IsHighRisk == true)
        {
            return RejectionReason.PropagationRiskTooHigh;
        }

        if (input.ForceReject || protections.Count > 0)
        {
            return RejectionReason.ManualReviewRequired;
        }

        return null;
    }
}
