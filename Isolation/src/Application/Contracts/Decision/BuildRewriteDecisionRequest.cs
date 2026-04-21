using Application.Contracts;
using Application.Contracts.Propagation;

namespace Application.Contracts.Decision;

/// <summary>
/// 构建改写决策请求。
/// </summary>
public sealed class BuildRewriteDecisionRequest
{
    public ChangeCandidateDto Candidate { get; set; } = new();

    public IReadOnlyCollection<string> ProtectionRules { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ConflictTargets { get; set; } = Array.Empty<string>();

    public ContractConfidenceLevel ConfidenceLevel { get; set; } = ContractConfidenceLevel.Medium;

    public bool ForceReject { get; set; }

    public ContractExposureDto? ContractExposure { get; set; }

    public ContractExternalCallerPresenceDto? ExternalCallerPresence { get; set; }

    public ContractClosureIntegrityAssessmentDto? ClosureIntegrityAssessment { get; set; }

    public ContractRiskScoreDto? RiskScore { get; set; }
}
