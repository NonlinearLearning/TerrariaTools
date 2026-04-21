using Domain.Rules;

namespace Domain.Decision;

/// <summary>
/// 表示改写决策解释输入。
/// </summary>
public sealed class RewriteDecisionResolutionInput
{
    public Guid CandidateId { get; init; }

    public ContractExposure? ContractExposure { get; init; }

    public ExternalCallerPresence? ExternalCallerPresence { get; init; }

    public ClosureIntegrityAssessment? ClosureIntegrityAssessment { get; init; }

    public DecisionRiskScore? RiskScore { get; init; }

    public IReadOnlyCollection<RuleCode> ProtectionRules { get; init; } = Array.Empty<RuleCode>();

    public IReadOnlyCollection<string> ConflictTargets { get; init; } = Array.Empty<string>();

    public bool ForceReject { get; init; }
}
