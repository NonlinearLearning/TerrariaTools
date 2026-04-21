namespace Domain.Decision;

/// <summary>
/// 表示改写决策评估结果。
/// </summary>
public sealed class RewriteDecisionAssessment
{
    public ContractExposure ContractExposure { get; init; } = ContractExposure.InternalOnly("local-only");

    public ExternalCallerPresence ExternalCallerPresence { get; init; } = ExternalCallerPresence.None();

    public ClosureIntegrityAssessment ClosureIntegrityAssessment { get; init; } =
        ClosureIntegrityAssessment.Verified("workflow propagation remained bounded");

    public DecisionRiskScore RiskScore { get; init; } = DecisionRiskScore.Low("bounded propagation");
}
