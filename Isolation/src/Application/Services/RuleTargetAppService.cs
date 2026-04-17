using Application.Abstractions;
using Application.Contracts.Marking;
using Application.Mappers;
using Domain.Marking;

namespace Application.Services;

/// <summary>
/// 规则命中目标应用服务实现。
/// </summary>
public sealed class RuleTargetAppService : IRuleTargetAppService
{
    private readonly IRuleTargetRepository ruleTargetRepository;

    public RuleTargetAppService(IRuleTargetRepository ruleTargetRepository)
    {
        this.ruleTargetRepository = ruleTargetRepository;
    }

    /// <inheritdoc />
    public async Task<RuleTargetDto> CreateAsync(
        CreateRuleTargetRequest request,
        CancellationToken cancellationToken = default)
    {
        RuleTarget ruleTarget = RuleTarget.Create(
            request.SnapshotId,
            request.RuleCode,
            ContractMapper.Map(request.Node),
            request.CandidateReason,
            request.Note);

        await ruleTargetRepository.AddAsync(ruleTarget, cancellationToken);
        return ContractMapper.Map(ruleTarget);
    }
}
