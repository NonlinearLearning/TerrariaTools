using Application.Contracts;
using Application.Contracts.Propagation;

namespace Application.Contracts.Decision;

/// <summary>
/// 改写决策 DTO。
/// </summary>
public sealed class RewriteDecisionDto
{
    public Guid Id { get; set; }

    public string DecisionName { get; set; } = string.Empty;

    public ContractConfidenceLevel ConfidenceLevel { get; set; }

    public IReadOnlyDictionary<Guid, ContractApprovalReason> Approvals { get; set; } =
        new Dictionary<Guid, ContractApprovalReason>();

    public IReadOnlyDictionary<Guid, ContractRejectionReason> Rejections { get; set; } =
        new Dictionary<Guid, ContractRejectionReason>();

    public IReadOnlyCollection<DecisionProtectionDto> Protections { get; set; } =
        Array.Empty<DecisionProtectionDto>();

    public IReadOnlyCollection<DecisionConflictDto> Conflicts { get; set; } =
        Array.Empty<DecisionConflictDto>();
}

/// <summary>
/// 保护项 DTO。
/// </summary>
public sealed class DecisionProtectionDto
{
    public Guid CandidateId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 冲突项 DTO。
/// </summary>
public sealed class DecisionConflictDto
{
    public Guid LeftCandidateId { get; set; }

    public Guid RightCandidateId { get; set; }

    public string Description { get; set; } = string.Empty;
}

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

/// <summary>
/// 决策结果 DTO。
/// </summary>
public sealed class DecisionResultDto
{
    public Guid CandidateId { get; set; }

    public RewriteDecisionDto Decision { get; set; } = new();

    public bool Approved { get; set; }

    public IReadOnlyCollection<DecisionProtectionDto> Protections { get; set; } =
        Array.Empty<DecisionProtectionDto>();

    public IReadOnlyCollection<DecisionConflictDto> Conflicts { get; set; } =
        Array.Empty<DecisionConflictDto>();
}
