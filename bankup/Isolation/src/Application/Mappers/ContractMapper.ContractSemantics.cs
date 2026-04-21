using Application.Contracts;
using Domain.Decision;
using Domain.Execution;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static ContractCandidateKind Map(CandidateKind candidateKind)
    {
        return candidateKind switch
        {
            CandidateKind.Type => ContractCandidateKind.Type,
            CandidateKind.Method => ContractCandidateKind.Method,
            CandidateKind.Member => ContractCandidateKind.Member,
            CandidateKind.Caller => ContractCandidateKind.Caller,
            CandidateKind.ClosureRoot => ContractCandidateKind.ClosureRoot,
            _ => ContractCandidateKind.Unknown,
        };
    }

    public static CandidateKind Map(ContractCandidateKind candidateKind)
    {
        return candidateKind switch
        {
            ContractCandidateKind.Type => CandidateKind.Type,
            ContractCandidateKind.Method => CandidateKind.Method,
            ContractCandidateKind.Member => CandidateKind.Member,
            ContractCandidateKind.Caller => CandidateKind.Caller,
            ContractCandidateKind.ClosureRoot => CandidateKind.ClosureRoot,
            _ => CandidateKind.Unknown,
        };
    }

    public static ContractCandidateReason Map(CandidateReason candidateReason)
    {
        return candidateReason switch
        {
            CandidateReason.CallChainMatched => ContractCandidateReason.CallChainMatched,
            CandidateReason.DataFlowReachable => ContractCandidateReason.DataFlowReachable,
            CandidateReason.CompositeLayerConflict => ContractCandidateReason.CompositeLayerConflict,
            CandidateReason.ManualReviewRequired => ContractCandidateReason.ManualReviewRequired,
            _ => ContractCandidateReason.Unknown,
        };
    }

    public static CandidateReason Map(ContractCandidateReason candidateReason)
    {
        return candidateReason switch
        {
            ContractCandidateReason.CallChainMatched => CandidateReason.CallChainMatched,
            ContractCandidateReason.DataFlowReachable => CandidateReason.DataFlowReachable,
            ContractCandidateReason.CompositeLayerConflict => CandidateReason.CompositeLayerConflict,
            ContractCandidateReason.ManualReviewRequired => CandidateReason.ManualReviewRequired,
            ContractCandidateReason.RuleConfigured => CandidateReason.ManualReviewRequired,
            ContractCandidateReason.EntryPointMatched => CandidateReason.CallChainMatched,
            ContractCandidateReason.PublicContractDetected => CandidateReason.ManualReviewRequired,
            ContractCandidateReason.RuntimeClosureRequired => CandidateReason.ManualReviewRequired,
            ContractCandidateReason.ShadowBoundaryRequired => CandidateReason.ManualReviewRequired,
            _ => CandidateReason.Unknown,
        };
    }

    public static ContractScenarioTag Map(ScenarioTag scenarioTag)
    {
        return scenarioTag switch
        {
            ScenarioTag.ClassDeletion => ContractScenarioTag.ClassDeletion,
            ScenarioTag.MethodDeletion => ContractScenarioTag.MethodDeletion,
            ScenarioTag.MethodPrivatization => ContractScenarioTag.MethodPrivatization,
            ScenarioTag.MethodBodyClearing => ContractScenarioTag.MethodBodyClearing,
            ScenarioTag.MemberSlice => ContractScenarioTag.MemberSlice,
            ScenarioTag.ShadowClassGeneration => ContractScenarioTag.ShadowClassGeneration,
            ScenarioTag.MinimalRuntimeClosure => ContractScenarioTag.MinimalRuntimeClosure,
            ScenarioTag.PlanDrivenRewrite => ContractScenarioTag.PlanDrivenRewrite,
            ScenarioTag.EvidenceDrivenAudit => ContractScenarioTag.EvidenceDrivenAudit,
            _ => ContractScenarioTag.Unknown,
        };
    }

    public static ScenarioTag Map(ContractScenarioTag scenarioTag)
    {
        return scenarioTag switch
        {
            ContractScenarioTag.ClassDeletion => ScenarioTag.ClassDeletion,
            ContractScenarioTag.MethodDeletion => ScenarioTag.MethodDeletion,
            ContractScenarioTag.MethodPrivatization => ScenarioTag.MethodPrivatization,
            ContractScenarioTag.MethodBodyClearing => ScenarioTag.MethodBodyClearing,
            ContractScenarioTag.MemberSlice => ScenarioTag.MemberSlice,
            ContractScenarioTag.ShadowClassGeneration => ScenarioTag.ShadowClassGeneration,
            ContractScenarioTag.MinimalRuntimeClosure => ScenarioTag.MinimalRuntimeClosure,
            ContractScenarioTag.PlanDrivenRewrite => ScenarioTag.PlanDrivenRewrite,
            ContractScenarioTag.EvidenceDrivenAudit => ScenarioTag.EvidenceDrivenAudit,
            _ => ScenarioTag.Unknown,
        };
    }

    public static ContractSliceDirection Map(SliceDirection sliceDirection)
    {
        return sliceDirection switch
        {
            SliceDirection.Forward => ContractSliceDirection.Forward,
            SliceDirection.Backward => ContractSliceDirection.Backward,
            SliceDirection.Bidirectional => ContractSliceDirection.Bidirectional,
            _ => ContractSliceDirection.Unknown,
        };
    }

    public static SliceDirection Map(ContractSliceDirection sliceDirection)
    {
        return sliceDirection switch
        {
            ContractSliceDirection.Forward => SliceDirection.Forward,
            ContractSliceDirection.Backward => SliceDirection.Backward,
            ContractSliceDirection.Bidirectional => SliceDirection.Bidirectional,
            _ => SliceDirection.Unknown,
        };
    }

    public static ContractConfidenceLevel Map(ConfidenceLevel confidenceLevel)
    {
        return confidenceLevel switch
        {
            ConfidenceLevel.Low => ContractConfidenceLevel.Low,
            ConfidenceLevel.Medium => ContractConfidenceLevel.Medium,
            ConfidenceLevel.High => ContractConfidenceLevel.High,
            _ => ContractConfidenceLevel.Unknown,
        };
    }

    public static ConfidenceLevel Map(ContractConfidenceLevel confidenceLevel)
    {
        return confidenceLevel switch
        {
            ContractConfidenceLevel.Low => ConfidenceLevel.Low,
            ContractConfidenceLevel.Medium => ConfidenceLevel.Medium,
            ContractConfidenceLevel.High => ConfidenceLevel.High,
            _ => ConfidenceLevel.Unknown,
        };
    }

    public static ContractApprovalReason Map(ApprovalReason approvalReason)
    {
        return approvalReason switch
        {
            ApprovalReason.StaticFactConfirmed => ContractApprovalReason.StaticFactConfirmed,
            ApprovalReason.PropagationBounded => ContractApprovalReason.PropagationBounded,
            ApprovalReason.CoveredByParentDeletion => ContractApprovalReason.CoveredByParentDeletion,
            ApprovalReason.ClosureIntegrityVerified => ContractApprovalReason.ClosureIntegrityVerified,
            ApprovalReason.ShadowBoundaryStable => ContractApprovalReason.ShadowBoundaryStable,
            _ => ContractApprovalReason.Unknown,
        };
    }

    public static ApprovalReason Map(ContractApprovalReason approvalReason)
    {
        return approvalReason switch
        {
            ContractApprovalReason.StaticFactConfirmed => ApprovalReason.StaticFactConfirmed,
            ContractApprovalReason.PropagationBounded => ApprovalReason.PropagationBounded,
            ContractApprovalReason.CoveredByParentDeletion => ApprovalReason.CoveredByParentDeletion,
            ContractApprovalReason.ClosureIntegrityVerified => ApprovalReason.ClosureIntegrityVerified,
            ContractApprovalReason.ShadowBoundaryStable => ApprovalReason.ShadowBoundaryStable,
            _ => ApprovalReason.Unknown,
        };
    }

    public static ContractRejectionReason Map(RejectionReason rejectionReason)
    {
        return rejectionReason switch
        {
            RejectionReason.ExternalContractDetected => ContractRejectionReason.ExternalContractDetected,
            RejectionReason.ExternalCallerDetected => ContractRejectionReason.ExternalCallerDetected,
            RejectionReason.ClosureIntegrityBroken => ContractRejectionReason.ClosureIntegrityBroken,
            RejectionReason.PropagationRiskTooHigh => ContractRejectionReason.PropagationRiskTooHigh,
            RejectionReason.ManualReviewRequired => ContractRejectionReason.ManualReviewRequired,
            _ => ContractRejectionReason.Unknown,
        };
    }

    public static RejectionReason Map(ContractRejectionReason rejectionReason)
    {
        return rejectionReason switch
        {
            ContractRejectionReason.ExternalContractDetected => RejectionReason.ExternalContractDetected,
            ContractRejectionReason.ExternalCallerDetected => RejectionReason.ExternalCallerDetected,
            ContractRejectionReason.ClosureIntegrityBroken => RejectionReason.ClosureIntegrityBroken,
            ContractRejectionReason.PropagationRiskTooHigh => RejectionReason.PropagationRiskTooHigh,
            ContractRejectionReason.ManualReviewRequired => RejectionReason.ManualReviewRequired,
            _ => RejectionReason.Unknown,
        };
    }

    public static ContractPlanAction Map(PlanAction planAction)
    {
        return planAction switch
        {
            PlanAction.DeleteClass => ContractPlanAction.DeleteClass,
            PlanAction.DeleteMethod => ContractPlanAction.DeleteMethod,
            PlanAction.PrivatizeMethod => ContractPlanAction.PrivatizeMethod,
            PlanAction.ClearMethodBody => ContractPlanAction.ClearMethodBody,
            PlanAction.SliceMember => ContractPlanAction.SliceMember,
            PlanAction.GenerateShadowClass => ContractPlanAction.GenerateShadowClass,
            PlanAction.ExtractRuntimeClosure => ContractPlanAction.ExtractRuntimeClosure,
            _ => ContractPlanAction.Unknown,
        };
    }

    public static PlanAction Map(ContractPlanAction planAction)
    {
        return planAction switch
        {
            ContractPlanAction.DeleteClass => PlanAction.DeleteClass,
            ContractPlanAction.DeleteMethod => PlanAction.DeleteMethod,
            ContractPlanAction.PrivatizeMethod => PlanAction.PrivatizeMethod,
            ContractPlanAction.ClearMethodBody => PlanAction.ClearMethodBody,
            ContractPlanAction.SliceMember => PlanAction.SliceMember,
            ContractPlanAction.GenerateShadowClass => PlanAction.GenerateShadowClass,
            ContractPlanAction.ExtractRuntimeClosure => PlanAction.ExtractRuntimeClosure,
            _ => PlanAction.Unknown,
        };
    }

    public static ContractPlanReason Map(PlanReason planReason)
    {
        return planReason switch
        {
            PlanReason.CandidateApproved => ContractPlanReason.CandidateApproved,
            PlanReason.LinkedActionDetected => ContractPlanReason.LinkedActionDetected,
            PlanReason.ParentCoverageResolved => ContractPlanReason.ParentCoverageResolved,
            PlanReason.ClosureBoundaryRequired => ContractPlanReason.ClosureBoundaryRequired,
            PlanReason.ShadowBoundaryRequired => ContractPlanReason.ShadowBoundaryRequired,
            _ => ContractPlanReason.Unknown,
        };
    }

    public static PlanReason Map(ContractPlanReason planReason)
    {
        return planReason switch
        {
            ContractPlanReason.CandidateApproved => PlanReason.CandidateApproved,
            ContractPlanReason.LinkedActionDetected => PlanReason.LinkedActionDetected,
            ContractPlanReason.ParentCoverageResolved => PlanReason.ParentCoverageResolved,
            ContractPlanReason.ClosureBoundaryRequired => PlanReason.ClosureBoundaryRequired,
            ContractPlanReason.ShadowBoundaryRequired => PlanReason.ShadowBoundaryRequired,
            _ => PlanReason.Unknown,
        };
    }

    public static ContractPlanConflict Map(PlanConflict planConflict)
    {
        return planConflict switch
        {
            PlanConflict.None => ContractPlanConflict.None,
            PlanConflict.DuplicateTarget => ContractPlanConflict.DuplicateTarget,
            PlanConflict.OverlappingRange => ContractPlanConflict.OverlappingRange,
            PlanConflict.ParentCoverage => ContractPlanConflict.ParentCoverage,
            PlanConflict.MutuallyExclusiveAction => ContractPlanConflict.MutuallyExclusiveAction,
            _ => ContractPlanConflict.None,
        };
    }

    public static PlanConflict Map(ContractPlanConflict planConflict)
    {
        return planConflict switch
        {
            ContractPlanConflict.None => PlanConflict.None,
            ContractPlanConflict.DuplicateTarget => PlanConflict.DuplicateTarget,
            ContractPlanConflict.OverlappingRange => PlanConflict.OverlappingRange,
            ContractPlanConflict.ParentCoverage => PlanConflict.ParentCoverage,
            ContractPlanConflict.MutuallyExclusiveAction => PlanConflict.MutuallyExclusiveAction,
            _ => PlanConflict.None,
        };
    }

    public static ContractRunMode Map(RunMode runMode)
    {
        return runMode switch
        {
            RunMode.AnalysisOnly => ContractRunMode.AnalysisOnly,
            RunMode.DecisionOnly => ContractRunMode.DecisionOnly,
            RunMode.FullWorkflow => ContractRunMode.FullWorkflow,
            _ => ContractRunMode.Unknown,
        };
    }

    public static RunMode Map(ContractRunMode runMode)
    {
        return runMode switch
        {
            ContractRunMode.AnalysisOnly => RunMode.AnalysisOnly,
            ContractRunMode.DecisionOnly => RunMode.DecisionOnly,
            ContractRunMode.FullWorkflow => RunMode.FullWorkflow,
            _ => RunMode.Unknown,
        };
    }

    public static ContractInputOrigin Map(InputOrigin inputOrigin)
    {
        return inputOrigin switch
        {
            InputOrigin.Solution => ContractInputOrigin.Solution,
            InputOrigin.Project => ContractInputOrigin.Project,
            InputOrigin.Directory => ContractInputOrigin.Directory,
            InputOrigin.SourceFile => ContractInputOrigin.SourceFile,
            _ => ContractInputOrigin.Unknown,
        };
    }

    public static ContractExposureDto? Map(ContractExposure? contractExposure)
    {
        return contractExposure is null
            ? null
            : new ContractExposureDto(contractExposure.IsPublicSurface, contractExposure.Source);
    }

    public static ContractExposure? Map(ContractExposureDto? contractExposure)
    {
        if (contractExposure is null)
        {
            return null;
        }

        return contractExposure.IsPublicSurface
            ? ContractExposure.PublicSurface(contractExposure.Source)
            : ContractExposure.InternalOnly(contractExposure.Source);
    }

    public static ContractExternalCallerPresenceDto? Map(ExternalCallerPresence? externalCallerPresence)
    {
        return externalCallerPresence is null
            ? null
            : new ContractExternalCallerPresenceDto(externalCallerPresence.Callers.ToArray());
    }

    public static ExternalCallerPresence? Map(ContractExternalCallerPresenceDto? externalCallerPresence)
    {
        if (externalCallerPresence is null)
        {
            return null;
        }

        return externalCallerPresence.Exists
            ? ExternalCallerPresence.Detected(externalCallerPresence.Callers)
            : ExternalCallerPresence.None();
    }

    public static ContractClosureIntegrityAssessmentDto? Map(ClosureIntegrityAssessment? closureIntegrityAssessment)
    {
        return closureIntegrityAssessment is null
            ? null
            : new ContractClosureIntegrityAssessmentDto(
                closureIntegrityAssessment.IsBroken,
                closureIntegrityAssessment.Summary);
    }

    public static ClosureIntegrityAssessment? Map(ContractClosureIntegrityAssessmentDto? closureIntegrityAssessment)
    {
        if (closureIntegrityAssessment is null)
        {
            return null;
        }

        return closureIntegrityAssessment.IsBroken
            ? ClosureIntegrityAssessment.Broken(closureIntegrityAssessment.Summary)
            : ClosureIntegrityAssessment.Verified(closureIntegrityAssessment.Summary);
    }

    public static ContractRiskScoreDto? Map(DecisionRiskScore? riskScore)
    {
        return riskScore is null
            ? null
            : new ContractRiskScoreDto(riskScore.Score, riskScore.Reason);
    }

    public static DecisionRiskScore? Map(ContractRiskScoreDto? riskScore)
    {
        if (riskScore is null)
        {
            return null;
        }

        if (riskScore.Score >= 80)
        {
            return DecisionRiskScore.High(riskScore.Reason);
        }

        if (riskScore.Score >= 40)
        {
            return DecisionRiskScore.Medium(riskScore.Reason);
        }

        return DecisionRiskScore.Low(riskScore.Reason);
    }
}
