using Application.Abstractions;
using Application.Contracts.Decision;
using Application.Mappers;
using Domain.Decision;

namespace Application.Services;

/// <summary>
/// 决策应用服务实现。
/// </summary>
public sealed class DecisionAppService : IDecisionAppService
{
    /// <inheritdoc />
    public Task<DecisionResultDto> DecideAsync(
        BuildRewriteDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RewriteDecision decision = RewriteDecision.Create(
            $"{request.Candidate.TargetName}-decision",
            request.ConfidenceLevel);

        foreach (string rule in request.ProtectionRules)
        {
            decision.AddProtection(new DecisionProtection(
                request.Candidate.Id,
                rule,
                $"保护规则 {rule} 阻止直接激进改写。"));
        }

        foreach (string conflictTarget in request.ConflictTargets)
        {
            decision.AddConflict(new DecisionConflict(
                request.Candidate.Id,
                Guid.NewGuid(),
                $"与 {conflictTarget} 存在计划冲突。"));
        }

        bool approved = !request.ForceReject && request.ProtectionRules.Count == 0;
        if (approved)
        {
            decision.Approve(request.Candidate.Id, ApprovalReason.PropagationBounded);
        }
        else
        {
            decision.Reject(request.Candidate.Id, RejectionReason.ManualReviewRequired);
        }

        RewriteDecisionDto decisionDto = ContractMapper.Map(decision);
        return Task.FromResult(new DecisionResultDto
        {
            CandidateId = request.Candidate.Id,
            Decision = decisionDto,
            Approved = approved,
            Protections = decisionDto.Protections,
            Conflicts = decisionDto.Conflicts,
        });
    }
}
