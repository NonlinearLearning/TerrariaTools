using Application.Abstractions;
using Application.Contracts;
using Application.Contracts.Decision;
using Application.Mappers;
using Logic.Decision;

namespace Application.Services;

/// <summary>
/// 决策应用服务实现。
/// </summary>
public sealed class DecisionAppService : IDecisionAppService
{
    private readonly IRewriteDecisionMaker rewriteDecisionMaker;

    public DecisionAppService(IRewriteDecisionMaker rewriteDecisionMaker)
    {
        this.rewriteDecisionMaker = rewriteDecisionMaker;
    }


    public Task<DecisionResultDto> DecideAsync(
        BuildRewriteDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RewriteDecisionResolution resolution = rewriteDecisionMaker.Make(new RewriteDecisionBuildInput
        {
            CandidateId = request.Candidate.Id,
            TargetName = request.Candidate.TargetName,
            ProtectionRules = request.ProtectionRules,
            ConflictTargets = request.ConflictTargets,
            ConfidenceLevel = ContractMapper.Map(request.ConfidenceLevel),
            ForceReject = request.ForceReject,
            ContractExposure = ContractMapper.Map(request.ContractExposure),
            ExternalCallerPresence = ContractMapper.Map(request.ExternalCallerPresence),
            ClosureIntegrityAssessment = ContractMapper.Map(request.ClosureIntegrityAssessment),
            RiskScore = ContractMapper.Map(request.RiskScore),
        });

        RewriteDecisionDto decisionDto = ContractMapper.Map(resolution.Decision);
        return Task.FromResult(new DecisionResultDto
        {
            CandidateId = resolution.CandidateId,
            Decision = decisionDto,
            Approved = resolution.Approved,
            Protections = decisionDto.Protections,
            Conflicts = decisionDto.Conflicts,
        });
    }
}
