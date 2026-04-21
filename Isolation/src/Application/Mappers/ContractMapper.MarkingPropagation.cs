using Application.Contracts.Marking;
using Application.Contracts.Propagation;
using Application.Contracts;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static RuleTargetDto Map(RuleTarget ruleTarget)
    {
        return new RuleTargetDto
        {
            Id = ruleTarget.Id,
            SnapshotId = ruleTarget.SnapshotId,
            RuleCode = ruleTarget.RuleCode.Value,
            CandidateReason = Map(ruleTarget.CandidateReason),
            Node = Map(ruleTarget.Node),
            Note = ruleTarget.Note,
        };
    }

    public static ChangeCandidateDto Map(ChangeCandidate changeCandidate)
    {
        return new ChangeCandidateDto
        {
            Id = changeCandidate.Id,
            RuleTargetId = changeCandidate.RuleTargetId,
            RuleCode = changeCandidate.RuleCode.Value,
            TargetName = changeCandidate.TargetName,
            CandidateKind = Map(changeCandidate.CandidateKind),
            Reasons = changeCandidate.Reasons.Select(Map).ToArray(),
            ScenarioTags = changeCandidate.ScenarioTags.Select(Map).ToArray(),
            IsCoveredByParentAction = changeCandidate.IsCoveredByParentAction,
            CoveredByCandidateId = changeCandidate.CoveredByCandidateId,
        };
    }

    public static ChangeCandidate Map(ChangeCandidateDto changeCandidate)
    {
        ChangeCandidate candidate = ChangeCandidate.Create(
            changeCandidate.RuleTargetId,
            RuleCode.Create(changeCandidate.RuleCode),
            changeCandidate.TargetName,
            Map(changeCandidate.CandidateKind),
            Map(changeCandidate.Reasons.FirstOrDefault()),
            Map(changeCandidate.ScenarioTags.FirstOrDefault()));

        foreach (ContractCandidateReason current in changeCandidate.Reasons.Skip(1))
        {
            candidate.AddReason(Map(current));
        }

        foreach (ContractScenarioTag current in changeCandidate.ScenarioTags.Skip(1))
        {
            candidate.AddScenarioTag(Map(current));
        }

        if (changeCandidate.CoveredByCandidateId.HasValue)
        {
            candidate.MarkCoveredByParentAction(changeCandidate.CoveredByCandidateId.Value);
        }

        return candidate;
    }

    public static SliceBoundaryDto Map(SliceBoundary sliceBoundary)
    {
        return new SliceBoundaryDto
        {
            BoundaryName = sliceBoundary.BoundaryName,
            Direction = Map(sliceBoundary.Direction),
            MaxDepth = sliceBoundary.MaxDepth,
            IncludeExternalReferences = sliceBoundary.IncludeExternalReferences,
        };
    }

    public static PropagationTraceDto Map(PropagationTrace propagationTrace)
    {
        return new PropagationTraceDto
        {
            SourceName = propagationTrace.SourceName,
            TargetName = propagationTrace.TargetName,
            Reason = propagationTrace.Reason,
            StepOrder = propagationTrace.StepOrder,
        };
    }

    public static PropagationFactReferenceDto Map(PropagationFactReference propagationFactReference)
    {
        return new PropagationFactReferenceDto
        {
            SourceNodeId = propagationFactReference.SourceNodeId,
            TargetNodeId = propagationFactReference.TargetNodeId,
            Kind = propagationFactReference.Kind,
        };
    }

    public static ShadowBoundaryDto Map(ShadowBoundary shadowBoundary)
    {
        return new ShadowBoundaryDto
        {
            ReferenceMappings = shadowBoundary.ReferenceMappings.Select(Map).ToArray(),
        };
    }

    public static RuntimeClosureBoundaryDto Map(RuntimeClosureBoundary runtimeClosureBoundary)
    {
        return new RuntimeClosureBoundaryDto
        {
            Root = Map(runtimeClosureBoundary.Root),
            IntegrityStatus = runtimeClosureBoundary.IntegrityStatus switch
            {
                ClosureIntegrityStatus.Verified => ContractClosureIntegrityStatus.Intact,
                ClosureIntegrityStatus.Risky => ContractClosureIntegrityStatus.Unknown,
                ClosureIntegrityStatus.Broken => ContractClosureIntegrityStatus.Broken,
                _ => ContractClosureIntegrityStatus.Unknown,
            },
            ReferenceMappings = runtimeClosureBoundary.ReferenceMappings.Select(Map).ToArray(),
        };
    }

    public static ClosureRootDto Map(ClosureRoot closureRoot)
    {
        return new ClosureRootDto
        {
            ClassName = closureRoot.ClassName,
            MemberName = closureRoot.MemberName,
        };
    }

    public static ReferenceMappingDto Map(ReferenceMapping referenceMapping)
    {
        return new ReferenceMappingDto
        {
            SourceReference = referenceMapping.SourceReference,
            TargetReference = referenceMapping.TargetReference,
        };
    }
}
