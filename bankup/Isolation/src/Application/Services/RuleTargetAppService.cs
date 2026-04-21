using Application.Abstractions;
using Application.Contracts;
using Application.Contracts.Marking;
using Application.Mappers;
using Domain.Analysis;
using Domain.Marking;
using Domain.Rules;
using Logic.Marking;
using Logic.Marking.Events;
using Logic.Rules;

namespace Application.Services;

/// <summary>
/// 规则命中目标应用服务实现。
/// </summary>
public sealed class RuleTargetAppService : IRuleTargetAppService
{
    private readonly IRuleTargetBuilder ruleTargetBuilder;
    private readonly IMarkingRulePreset markingRulePreset;
    private readonly IRuleTargetRepository ruleTargetRepository;
    private readonly IRuleTargetMarkingPreparer? ruleTargetMarkingPreparer;
    private readonly IAnalysisSnapshotRepository? analysisSnapshotRepository;
    private readonly IMarkingDomainEventPublisher? markingDomainEventPublisher;

    public RuleTargetAppService(
        IRuleTargetBuilder ruleTargetBuilder,
        IMarkingRulePreset markingRulePreset,
        IRuleTargetRepository ruleTargetRepository,
        IRuleTargetMarkingPreparer? ruleTargetMarkingPreparer = null,
        IAnalysisSnapshotRepository? analysisSnapshotRepository = null,
        IMarkingDomainEventPublisher? markingDomainEventPublisher = null)
    {
        this.ruleTargetBuilder = ruleTargetBuilder;
        this.markingRulePreset = markingRulePreset;
        this.ruleTargetRepository = ruleTargetRepository;
        this.ruleTargetMarkingPreparer = ruleTargetMarkingPreparer;
        this.analysisSnapshotRepository = analysisSnapshotRepository;
        this.markingDomainEventPublisher = markingDomainEventPublisher;
    }


    public async Task<RuleTargetDto> CreateAsync(
        CreateRuleTargetRequest request,
        CancellationToken cancellationToken = default)
    {
        RuleCode markingRuleCode = markingRulePreset.ResolveRuleCode(request.RuleCode);
        RuleTarget ruleTarget = ruleTargetBuilder.Build(new RuleTargetBuildInput
        {
            SnapshotId = request.SnapshotId,
            RuleCode = markingRuleCode,
            Node = ContractMapper.Map(request.Node),
            CandidateReason = ContractMapper.Map(request.CandidateReason),
            Note = request.Note,
        });

        await ruleTargetRepository.AddAsync(ruleTarget, cancellationToken);
        if (markingDomainEventPublisher is not null)
        {
            Guid workspaceContextId = await ResolveWorkspaceContextIdAsync(ruleTarget.SnapshotId, cancellationToken);
            IReadOnlyCollection<Domain.Propagation.ChangeCandidate> candidates = BuildCandidates(ruleTarget);
            markingDomainEventPublisher.Publish(new MarkingDomainEventPublishInput
            {
                RunCorrelationId = request.RunCorrelationId,
                WorkspaceContextId = workspaceContextId,
                RuleTarget = ruleTarget,
                Candidates = candidates,
            });
        }

        return ContractMapper.Map(ruleTarget);
    }

    private IReadOnlyCollection<Domain.Propagation.ChangeCandidate> BuildCandidates(RuleTarget ruleTarget)
    {
        if (ruleTargetMarkingPreparer is null)
        {
            return Array.Empty<Domain.Propagation.ChangeCandidate>();
        }

        return ruleTargetMarkingPreparer.Prepare(ruleTarget);
    }

    private async Task<Guid> ResolveWorkspaceContextIdAsync(Guid snapshotId, CancellationToken cancellationToken)
    {
        if (analysisSnapshotRepository is null)
        {
            return snapshotId;
        }

        AnalysisCpgSnapshot? snapshot = await analysisSnapshotRepository.GetCpgSnapshotAsync(snapshotId, cancellationToken);
        return snapshot?.WorkspaceContextId ?? snapshotId;
    }
}
