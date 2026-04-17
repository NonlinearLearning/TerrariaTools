using Application.Contracts.Analysis;
using Application.Contracts.Decision;
using Application.Contracts.Execution;
using Application.Contracts.Marking;
using Application.Contracts.Output;
using Application.Contracts.Propagation;
using Application.Contracts.Rewrite;
using Application.Contracts.Workspaces;
using Domain.Analysis;
using Domain.Decision;
using Domain.Execution;
using Domain.Marking;
using Domain.Output;
using Domain.Propagation;
using Domain.Rewrite;
using Domain.Workspaces;

namespace Application.Mappers;

/// <summary>
/// 负责领域对象与 DTO 的映射。
/// </summary>
public static class ContractMapper
{
    public static WorkspaceContextDto Map(WorkspaceContext workspaceContext)
    {
        return new WorkspaceContextDto
        {
            Id = workspaceContext.Id,
            SolutionPath = workspaceContext.SolutionPath,
            LanguageVersion = workspaceContext.LanguageVersion,
            Projects = workspaceContext.Projects
                .Select(item => new ProjectItemDto { Name = item.Name, Path = item.Path })
                .ToArray(),
            Documents = workspaceContext.Documents.Select(item => item.Value).ToArray(),
            References = workspaceContext.References
                .Select(item => new ReferenceItemDto { Name = item.Name, Version = item.Version })
                .ToArray(),
        };
    }

    public static AnalysisCpgSnapshotDto Map(AnalysisCpgSnapshot snapshot)
    {
        return new AnalysisCpgSnapshotDto
        {
            Id = snapshot.Id,
            WorkspaceContextId = snapshot.WorkspaceContextId,
            MinimumTarget = snapshot.MinimumTarget,
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
            Nodes = snapshot.Nodes.Select(Map).ToArray(),
        };
    }

    public static AnalysisCompositeLayerSnapshotDto Map(AnalysisCompositeLayerSnapshot snapshot)
    {
        return new AnalysisCompositeLayerSnapshotDto
        {
            Id = snapshot.Id,
            CompositionName = snapshot.CompositionName,
            Depth = snapshot.Depth,
            LayerNames = snapshot.LayerNames.ToArray(),
            Nodes = snapshot.Nodes.Select(Map).ToArray(),
        };
    }

    public static RuleTargetDto Map(RuleTarget ruleTarget)
    {
        return new RuleTargetDto
        {
            Id = ruleTarget.Id,
            SnapshotId = ruleTarget.SnapshotId,
            RuleCode = ruleTarget.RuleCode,
            CandidateReason = ruleTarget.CandidateReason,
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
            RuleCode = changeCandidate.RuleCode,
            TargetName = changeCandidate.TargetName,
            CandidateKind = changeCandidate.CandidateKind,
            Reasons = changeCandidate.Reasons.ToArray(),
            ScenarioTags = changeCandidate.ScenarioTags.ToArray(),
            IsCoveredByParentAction = changeCandidate.IsCoveredByParentAction,
            CoveredByCandidateId = changeCandidate.CoveredByCandidateId,
        };
    }

    public static CodeRewriteResultDto Map(CodeRewriteResult result)
    {
        return new CodeRewriteResultDto
        {
            RewriteKind = result.RewriteKind,
            TargetName = result.TargetName,
            SourceCode = result.SourceCode,
            Changed = result.Changed,
            Diagnostics = result.Diagnostics.ToArray(),
        };
    }

    public static MemberSliceDto Map(MemberSlice memberSlice)
    {
        return new MemberSliceDto
        {
            ClassName = memberSlice.ClassName,
            RootMemberName = memberSlice.RootMemberName,
            SourceCode = memberSlice.SourceCode,
            MemberNames = memberSlice.MemberNames.ToArray(),
        };
    }

    public static ShadowClassDto Map(ShadowClass shadowClass)
    {
        return new ShadowClassDto
        {
            ClassName = shadowClass.ClassName,
            ShadowClassName = shadowClass.ShadowClassName,
            SourceCode = shadowClass.SourceCode,
            MemberNames = shadowClass.MemberNames.ToArray(),
            ReferenceMappings = shadowClass.ReferenceMappings.Select(Map).ToArray(),
        };
    }

    public static RuntimeClosureDto Map(RuntimeClosure runtimeClosure)
    {
        return new RuntimeClosureDto
        {
            ClassName = runtimeClosure.ClassName,
            RootMethodName = runtimeClosure.RootMethodName,
            ClosureClassName = runtimeClosure.ClosureClassName,
            SourceCode = runtimeClosure.SourceCode,
            MemberNames = runtimeClosure.MemberNames.ToArray(),
            IntegrityStatus = runtimeClosure.IntegrityStatus,
            ReferenceMappings = runtimeClosure.ReferenceMappings.Select(Map).ToArray(),
        };
    }

    public static SliceBoundaryDto Map(SliceBoundary sliceBoundary)
    {
        return new SliceBoundaryDto
        {
            BoundaryName = sliceBoundary.BoundaryName,
            Direction = sliceBoundary.Direction,
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

    public static ReferenceMappingDto Map(ReferenceMapping referenceMapping)
    {
        return new ReferenceMappingDto
        {
            SourceReference = referenceMapping.SourceReference,
            TargetReference = referenceMapping.TargetReference,
        };
    }

    public static RewriteDecisionDto Map(RewriteDecision rewriteDecision)
    {
        return new RewriteDecisionDto
        {
            Id = rewriteDecision.Id,
            DecisionName = rewriteDecision.DecisionName,
            ConfidenceLevel = rewriteDecision.ConfidenceLevel,
            Approvals = new Dictionary<Guid, ApprovalReason>(rewriteDecision.Approvals),
            Rejections = new Dictionary<Guid, RejectionReason>(rewriteDecision.Rejections),
            Protections = rewriteDecision.Protections.Select(Map).ToArray(),
            Conflicts = rewriteDecision.Conflicts.Select(Map).ToArray(),
        };
    }

    public static DecisionProtectionDto Map(DecisionProtection decisionProtection)
    {
        return new DecisionProtectionDto
        {
            CandidateId = decisionProtection.CandidateId,
            RuleCode = decisionProtection.RuleCode,
            Description = decisionProtection.Description,
        };
    }

    public static DecisionConflictDto Map(DecisionConflict decisionConflict)
    {
        return new DecisionConflictDto
        {
            LeftCandidateId = decisionConflict.LeftCandidateId,
            RightCandidateId = decisionConflict.RightCandidateId,
            Description = decisionConflict.Description,
        };
    }

    public static RewritePlanDto Map(RewritePlan rewritePlan)
    {
        return new RewritePlanDto
        {
            Id = rewritePlan.Id,
            Metadata = Map(rewritePlan.Metadata),
            ChangeItems = rewritePlan.ChangeItems.Select(Map).ToArray(),
            Conflicts = rewritePlan.Conflicts.ToArray(),
        };
    }

    public static PlanMetadataDto Map(PlanMetadata planMetadata)
    {
        return new PlanMetadataDto
        {
            PlanName = planMetadata.PlanName,
            CompilerVersion = planMetadata.CompilerVersion,
            CreatedAt = planMetadata.CreatedAt,
            Note = planMetadata.Note,
        };
    }

    public static PlanChangeItemDto Map(PlanChangeItem planChangeItem)
    {
        return new PlanChangeItemDto
        {
            Id = planChangeItem.Id,
            CandidateId = planChangeItem.CandidateId,
            PlanTarget = Map(planChangeItem.PlanTarget),
            PlanAction = planChangeItem.PlanAction,
            Order = planChangeItem.Order,
            Reasons = planChangeItem.Reasons.ToArray(),
        };
    }

    public static PlanTargetDto Map(PlanTarget planTarget)
    {
        return new PlanTargetDto
        {
            DocumentPath = planTarget.DocumentPath.Value,
            TargetName = planTarget.TargetName,
            MemberSignature = planTarget.MemberSignature,
            AnchorText = planTarget.AnchorText,
        };
    }

    public static RewriteResultDto Map(RewriteResult rewriteResult)
    {
        return new RewriteResultDto
        {
            Id = rewriteResult.Id,
            RewritePlanId = rewriteResult.RewritePlanId,
            FileChanges = rewriteResult.FileChanges.Select(Map).ToArray(),
            ExecutionTraces = rewriteResult.ExecutionTraces.Select(Map).ToArray(),
            ExecutionFailures = rewriteResult.ExecutionFailures.Select(Map).ToArray(),
        };
    }

    public static FileChangeDto Map(FileChange fileChange)
    {
        return new FileChangeDto
        {
            DocumentPath = fileChange.DocumentPath.Value,
            Summary = fileChange.Summary,
            AffectedTargets = fileChange.AffectedTargets.ToArray(),
        };
    }

    public static ExecutionTraceDto Map(ExecutionTrace executionTrace)
    {
        return new ExecutionTraceDto
        {
            PlanChangeItemId = executionTrace.PlanChangeItemId,
            StepName = executionTrace.StepName,
            Message = executionTrace.Message,
            RecordedAt = executionTrace.RecordedAt,
        };
    }

    public static ExecutionFailureDto Map(ExecutionFailure executionFailure)
    {
        return new ExecutionFailureDto
        {
            PlanChangeItemId = executionFailure.PlanChangeItemId,
            FailureType = executionFailure.FailureType,
            Message = executionFailure.Message,
            Retryable = executionFailure.Retryable,
        };
    }

    public static VerificationEvidenceDto Map(VerificationEvidence verificationEvidence)
    {
        return new VerificationEvidenceDto
        {
            Id = verificationEvidence.Id,
            RewriteResultId = verificationEvidence.RewriteResultId,
            RiskSummary = Map(verificationEvidence.RiskSummary),
            CompilationEvidence = verificationEvidence.CompilationEvidence.Select(Map).ToArray(),
            StaticReasoningEvidence = verificationEvidence.StaticReasoningEvidence.Select(Map).ToArray(),
            BehaviorEvidence = verificationEvidence.BehaviorEvidence.Select(Map).ToArray(),
        };
    }

    public static RunReportDto Map(RunReport runReport)
    {
        return new RunReportDto
        {
            Id = runReport.Id,
            WorkspaceContextId = runReport.WorkspaceContextId,
            RewriteDecisionId = runReport.RewriteDecisionId,
            RewritePlanId = runReport.RewritePlanId,
            RewriteResultId = runReport.RewriteResultId,
            VerificationEvidenceId = runReport.VerificationEvidenceId,
            ReportSummary = Map(runReport.ReportSummary),
            AuditConclusion = runReport.AuditConclusion,
        };
    }

    public static CompilationEvidenceDto Map(CompilationEvidence compilationEvidence)
    {
        return new CompilationEvidenceDto
        {
            Success = compilationEvidence.Success,
            DiagnosticCount = compilationEvidence.DiagnosticCount,
            Summary = compilationEvidence.Summary,
        };
    }

    public static StaticReasoningEvidenceDto Map(StaticReasoningEvidence staticReasoningEvidence)
    {
        return new StaticReasoningEvidenceDto
        {
            SubjectName = staticReasoningEvidence.SubjectName,
            Summary = staticReasoningEvidence.Summary,
        };
    }

    public static BehaviorEvidenceDto Map(BehaviorEvidence behaviorEvidence)
    {
        return new BehaviorEvidenceDto
        {
            ScenarioName = behaviorEvidence.ScenarioName,
            Passed = behaviorEvidence.Passed,
            Summary = behaviorEvidence.Summary,
        };
    }

    public static RiskSummaryDto Map(RiskSummary riskSummary)
    {
        return new RiskSummaryDto
        {
            LevelName = riskSummary.LevelName,
            RequiresManualReview = riskSummary.RequiresManualReview,
            Items = riskSummary.Items.ToArray(),
        };
    }

    public static ReportSummaryDto Map(ReportSummary reportSummary)
    {
        return new ReportSummaryDto
        {
            ApprovedCount = reportSummary.ApprovedCount,
            RejectedCount = reportSummary.RejectedCount,
            FailureCount = reportSummary.FailureCount,
            Highlights = reportSummary.Highlights,
        };
    }

    public static MinimumNodeDto Map(MinimumNode node)
    {
        return new MinimumNodeDto
        {
            NodeId = node.NodeId,
            DisplayName = node.DisplayName,
            NodeType = node.NodeType,
            DocumentPath = node.LocationRange.DocumentPath,
            StartLine = node.LocationRange.StartLine,
            StartColumn = node.LocationRange.StartColumn,
            EndLine = node.LocationRange.EndLine,
            EndColumn = node.LocationRange.EndColumn,
        };
    }

    public static MinimumNode Map(MinimumNodeDto node)
    {
        return new MinimumNode(
            node.NodeId,
            node.DisplayName,
            node.NodeType,
            new LocationRange(
                node.DocumentPath,
                node.StartLine,
                node.StartColumn,
                node.EndLine,
                node.EndColumn));
    }
}
