using Application.Contracts;
using Application.Contracts.Workflow;
using Application.Mappers;
using Domain.Analysis;
using Domain.Decision;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Decision;
using Logic.Propagation;
using Logic.Rules;
using Logic.Workflow;

namespace Application.Services.RewriteWorkflow;

internal static class RewriteWorkflowStageInputFactory
{
    public static RewriteWorkflowPropagationStageInput BuildPropagation(
        RunRewriteWorkflowRequest request,
        RuleCode workflowRuleCode,
        AnalysisCpgSnapshot? analysisSnapshot,
        ChangeCandidate? markingCandidate)
    {
        return RewriteWorkflowPropagationStageInput.Create(
            request.RuleTargetId,
            workflowRuleCode.Value,
            request.TargetName,
            ContractMapper.Map(request.CandidateKind),
            ContractMapper.Map(request.PrimaryReason),
            request.AdditionalReasons.Select(ContractMapper.Map).ToArray(),
            request.ScenarioTags.Select(ContractMapper.Map).ToArray(),
            request.BoundaryName,
            ContractMapper.Map(request.SliceDirection),
            request.MaxDepth,
            request.IncludeExternalReferences,
            request.PropagationTargets,
            markingCandidate,
            analysisSnapshot);
    }

    public static RewriteWorkflowDecisionStageInput BuildDecision(
        RunRewriteWorkflowRequest request,
        PropagationResolution propagation,
        IRewriteWorkflowRulePreset rewriteWorkflowRulePreset)
    {
        return RewriteWorkflowDecisionStageInput.Create(
            propagation,
            request.IncludeExternalReferences,
            request.SimulateFailure,
            rewriteWorkflowRulePreset.NormalizeProtectionRules(request.ProtectionRules),
            request.ConflictTargets,
            ContractMapper.Map(request.ConfidenceLevel),
            request.ForceReject);
    }

    public static RewriteWorkflowAssemblyInput BuildAssembly(
        RunRewriteWorkflowRequest request,
        RuleCode workflowRuleCode,
        Guid runCorrelationId,
        WorkspaceContext workspaceContext,
        PropagationResolution propagation,
        RewriteDecisionResolution decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspaceContext);
        ArgumentNullException.ThrowIfNull(propagation);
        ArgumentNullException.ThrowIfNull(decision);

        return new RewriteWorkflowAssemblyInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContext = workspaceContext,
            WorkspaceContextId = request.WorkspaceContextId,
            AnalysisSnapshotId = request.AnalysisSnapshotId,
            RuleTargetId = request.RuleTargetId,
            RuleCode = workflowRuleCode.Value,
            CandidateId = propagation.Candidate.Id,
            DecisionId = decision.Decision.Id,
            Decision = decision.Decision,
            Approved = decision.Approved,
            ApprovalCount = decision.Decision.Approvals.Count,
            RejectionCount = decision.Decision.Rejections.Count,
            ProtectionCount = decision.Decision.Protections.Count,
            PropagationTraceCount = propagation.PropagationTraces.Count,
            ReasonCount = propagation.Candidate.Reasons.Count,
            MaxDepth = request.MaxDepth,
            ConflictTargetCount = request.ConflictTargets.Count,
            Target = new RewriteWorkflowTargetDescriptor
            {
                TargetName = request.TargetName,
                DocumentPath = request.DocumentPath,
                MemberSignature = request.MemberSignature,
                AnchorText = request.AnchorText,
            },
            PlanAction = ContractMapper.Map(request.PlanAction),
            PropagationTargets = request.PropagationTargets,
            Execution = new RewriteWorkflowExecutionDescriptor
            {
                SourceCode = request.SourceCode,
                ClassName = request.ClassName,
                MethodName = request.MethodName,
                ParameterCount = request.ParameterCount,
            },
        };
    }
}
