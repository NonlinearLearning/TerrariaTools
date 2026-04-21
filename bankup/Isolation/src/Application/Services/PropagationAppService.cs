using Application.Abstractions;
using Application.Contracts;
using Application.Contracts.Propagation;
using Application.Mappers;
using Domain.Analysis;
using Domain.Rules;
using Logic.Propagation;
using Logic.Propagation.Events;
using Logic.Rules;

namespace Application.Services;

/// <summary>
/// 传播应用服务实现。
/// </summary>
public sealed class PropagationAppService : IPropagationAppService
{
    private readonly IImpactPropagator impactPropagator;
    private readonly IPropagationRulePreset propagationRulePreset;
    private readonly IAnalysisSnapshotRepository analysisSnapshotRepository;
    private readonly IPropagationDomainEventPublisher? propagationDomainEventPublisher;

    public PropagationAppService(
        IImpactPropagator impactPropagator,
        IPropagationRulePreset propagationRulePreset,
        IAnalysisSnapshotRepository analysisSnapshotRepository,
        IPropagationDomainEventPublisher? propagationDomainEventPublisher = null)
    {
        this.impactPropagator = impactPropagator;
        this.propagationRulePreset = propagationRulePreset;
        this.analysisSnapshotRepository = analysisSnapshotRepository;
        this.propagationDomainEventPublisher = propagationDomainEventPublisher;
    }


    public async Task<PropagationResultDto> PropagateAsync(
        BuildPropagationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AnalysisCpgSnapshot? snapshot = request.AnalysisSnapshotId.HasValue
            ? await analysisSnapshotRepository.GetCpgSnapshotAsync(request.AnalysisSnapshotId.Value, cancellationToken)
            : null;
        RuleCode propagationRuleCode = propagationRulePreset.ResolveRuleCode(request.RuleCode);
        PropagationResolution resolution = impactPropagator.Propagate(new PropagationBuildInput
        {
            RuleTargetId = request.RuleTargetId,
            RuleCode = propagationRuleCode,
            TargetName = request.TargetName,
            CandidateKind = ContractMapper.Map(request.CandidateKind),
            PrimaryReason = ContractMapper.Map(request.PrimaryReason),
            AdditionalReasons = request.AdditionalReasons.Select(ContractMapper.Map).ToArray(),
            ScenarioTags = request.ScenarioTags.Select(ContractMapper.Map).ToArray(),
            BoundaryName = request.BoundaryName,
            SliceDirection = ContractMapper.Map(request.SliceDirection),
            MaxDepth = request.MaxDepth,
            IncludeExternalReferences = request.IncludeExternalReferences,
            PropagationTargets = request.PropagationTargets,
            Candidate = request.Candidate is null ? null : ContractMapper.Map(request.Candidate),
            Snapshot = snapshot,
        });
        propagationDomainEventPublisher?.Publish(new PropagationDomainEventPublishInput
        {
            RunCorrelationId = request.RunCorrelationId,
            WorkspaceContextId = snapshot?.WorkspaceContextId
                ?? request.WorkspaceContextId
                ?? request.RuleTargetId,
            Resolution = resolution,
        });

        return new PropagationResultDto
        {
            RunCorrelationId = request.RunCorrelationId,
            CandidateId = resolution.Candidate.Id,
            Candidate = ContractMapper.Map(resolution.Candidate),
            SliceBoundary = ContractMapper.Map(resolution.SliceBoundary),
            PropagationTraces = resolution.PropagationTraces.Select(ContractMapper.Map).ToArray(),
            FactReferences = resolution.FactReferences.Select(ContractMapper.Map).ToArray(),
        };
    }
}
