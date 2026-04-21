using Application.Contracts.Decision;
using Application.Contracts.Execution;
using Application.Contracts;
using Domain.Decision;
using Domain.Execution;
using Domain.Rules;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static RewriteDecisionDto Map(RewriteDecision rewriteDecision)
    {
        return new RewriteDecisionDto
        {
            Id = rewriteDecision.Id,
            DecisionName = rewriteDecision.DecisionName,
            ConfidenceLevel = Map(rewriteDecision.ConfidenceLevel),
            Approvals = rewriteDecision.Approvals.ToDictionary(item => item.Key, item => Map(item.Value)),
            Rejections = rewriteDecision.Rejections.ToDictionary(item => item.Key, item => Map(item.Value)),
            Protections = rewriteDecision.Protections.Select(Map).ToArray(),
            Conflicts = rewriteDecision.Conflicts.Select(Map).ToArray(),
        };
    }

    public static RewriteDecision Map(RewriteDecisionDto rewriteDecision)
    {
        RewriteDecision mapped = RewriteDecision.Create(rewriteDecision.DecisionName, Map(rewriteDecision.ConfidenceLevel));

        foreach ((Guid candidateId, ContractApprovalReason approvalReason) in rewriteDecision.Approvals)
        {
            mapped.Approve(candidateId, Map(approvalReason));
        }

        foreach ((Guid candidateId, ContractRejectionReason rejectionReason) in rewriteDecision.Rejections)
        {
            mapped.Reject(candidateId, Map(rejectionReason));
        }

        foreach (DecisionProtectionDto protection in rewriteDecision.Protections)
        {
            mapped.AddProtection(new DecisionProtection(protection.CandidateId, RuleCode.Create(protection.RuleCode), protection.Description));
        }

        foreach (DecisionConflictDto conflict in rewriteDecision.Conflicts)
        {
            mapped.AddConflict(new DecisionConflict(conflict.LeftCandidateId, conflict.RightCandidateId, conflict.Description));
        }

        return mapped;
    }

    public static DecisionProtectionDto Map(DecisionProtection decisionProtection)
    {
        return new DecisionProtectionDto
        {
            CandidateId = decisionProtection.CandidateId,
            RuleCode = decisionProtection.RuleCode.Value,
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
            Conflicts = rewritePlan.Conflicts.Select(Map).ToArray(),
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
            PlanAction = Map(planChangeItem.PlanAction),
            Order = planChangeItem.Order,
            Reasons = planChangeItem.Reasons.Select(Map).ToArray(),
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
}
