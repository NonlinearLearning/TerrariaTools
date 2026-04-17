using Application.Abstractions;
using Application.Contracts.Propagation;
using Application.Mappers;
using Domain.Marking;
using Domain.Propagation;

namespace Application.Services;

/// <summary>
/// 传播应用服务实现。
/// </summary>
public sealed class PropagationAppService : IPropagationAppService
{
    /// <inheritdoc />
    public Task<PropagationResultDto> PropagateAsync(
        BuildPropagationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ChangeCandidate candidate = ChangeCandidate.Create(
            request.RuleTargetId,
            request.RuleCode,
            request.TargetName,
            request.CandidateKind,
            request.PrimaryReason,
            request.ScenarioTags.FirstOrDefault());
        SliceBoundary sliceBoundary = new(
            request.BoundaryName,
            request.SliceDirection,
            request.MaxDepth,
            request.IncludeExternalReferences);
        candidate.SetSliceBoundary(sliceBoundary);

        foreach (CandidateReason additionalReason in request.AdditionalReasons)
        {
            candidate.AddReason(additionalReason);
        }

        foreach (ScenarioTag scenarioTag in request.ScenarioTags.Skip(1))
        {
            candidate.AddScenarioTag(scenarioTag);
        }

        int stepOrder = 1;
        foreach (string target in request.PropagationTargets)
        {
            candidate.AddPropagationTrace(new PropagationTrace(
                request.TargetName,
                target,
                "传播阶段识别到联动目标。",
                stepOrder));
            stepOrder++;
        }

        return Task.FromResult(new PropagationResultDto
        {
            CandidateId = candidate.Id,
            Candidate = ContractMapper.Map(candidate),
            SliceBoundary = ContractMapper.Map(sliceBoundary),
            PropagationTraces = candidate.PropagationTraces.Select(ContractMapper.Map).ToArray(),
        });
    }
}
