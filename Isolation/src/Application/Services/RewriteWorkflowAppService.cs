using Application.Abstractions;
using Application.Contracts.Decision;
using Application.Contracts.Propagation;
using Application.Contracts.Workflow;
using Application.Mappers;
using Domain.Execution;
using Domain.Output;
using Domain.Workspaces;

namespace Application.Services;

/// <summary>
/// 改写工作流应用服务实现。
/// </summary>
public sealed class RewriteWorkflowAppService : IRewriteWorkflowAppService
{
    private readonly IPropagationAppService propagationAppService;
    private readonly IDecisionAppService decisionAppService;

    public RewriteWorkflowAppService(
        IPropagationAppService propagationAppService,
        IDecisionAppService decisionAppService)
    {
        this.propagationAppService = propagationAppService;
        this.decisionAppService = decisionAppService;
    }

    /// <inheritdoc />
    public async Task<RewriteWorkflowRunDto> RunAsync(
        RunRewriteWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PropagationResultDto propagation = await propagationAppService.PropagateAsync(
            new BuildPropagationRequest
            {
                RuleTargetId = request.RuleTargetId,
                RuleCode = request.RuleCode,
                TargetName = request.TargetName,
                CandidateKind = request.CandidateKind,
                PrimaryReason = request.PrimaryReason,
                AdditionalReasons = request.AdditionalReasons,
                ScenarioTags = request.ScenarioTags,
                BoundaryName = request.BoundaryName,
                SliceDirection = request.SliceDirection,
                MaxDepth = request.MaxDepth,
                IncludeExternalReferences = request.IncludeExternalReferences,
                PropagationTargets = request.PropagationTargets,
            },
            cancellationToken);
        DecisionResultDto decisionResult = await decisionAppService.DecideAsync(
            new BuildRewriteDecisionRequest
            {
                Candidate = propagation.Candidate,
                ProtectionRules = request.ProtectionRules,
                ConflictTargets = request.ConflictTargets,
                ConfidenceLevel = request.ConfidenceLevel,
                ForceReject = request.ForceReject,
            },
            cancellationToken);

        RewritePlan plan = BuildPlan(propagation, decisionResult, request);
        RewriteResult result = BuildResult(plan, request);
        VerificationEvidence evidence = BuildEvidence(result, decisionResult, request);
        RunReport report = BuildReport(request, decisionResult, plan, result, evidence);

        return new RewriteWorkflowRunDto
        {
            Propagation = propagation,
            Candidate = propagation.Candidate,
            DecisionResult = decisionResult,
            Decision = decisionResult.Decision,
            Plan = ContractMapper.Map(plan),
            Result = ContractMapper.Map(result),
            Evidence = ContractMapper.Map(evidence),
            Report = ContractMapper.Map(report),
        };
    }

    private static RewritePlan BuildPlan(
        PropagationResultDto propagation,
        DecisionResultDto decisionResult,
        RunRewriteWorkflowRequest request)
    {
        RewritePlan plan = RewritePlan.Create(new PlanMetadata(
            $"{request.TargetName}-plan",
            "workflow-app-service/1.0.0",
            DateTimeOffset.UtcNow,
            "由应用层工作流编排生成。"));

        PlanChangeItem item = PlanChangeItem.Create(
            propagation.CandidateId,
            new PlanTarget(
                DocumentPath.Create(request.DocumentPath),
                request.TargetName,
                request.MemberSignature,
                request.AnchorText),
            request.PlanAction,
            decisionResult.Approved
                ? PlanReason.CandidateApproved
                : PlanReason.LinkedActionDetected);

        if (propagation.PropagationTraces.Count > 0)
        {
            item.AddReason(PlanReason.ClosureBoundaryRequired);
        }

        plan.AddChangeItem(item);
        plan.OrderChangeItem(item.Id, 1);

        if (request.ConflictTargets.Count > 0)
        {
            plan.AddConflict(PlanConflict.ParentCoverage);
        }

        return plan;
    }

    private static RewriteResult BuildResult(RewritePlan plan, RunRewriteWorkflowRequest request)
    {
        RewriteResult result = RewriteResult.Create(plan.Id);
        PlanChangeItem planItem = plan.ChangeItems.Single();

        result.AddFileChange(new FileChange(
            DocumentPath.Create(request.DocumentPath),
            $"执行动作 {request.PlanAction}，目标 {request.TargetName}。",
            request.PropagationTargets.Prepend(request.TargetName).ToArray()));
        result.AddExecutionTrace(new ExecutionTrace(
            planItem.Id,
            "执行阶段",
            $"已生成并应用 {request.PlanAction} 计划。",
            DateTimeOffset.UtcNow));

        if (request.SimulateFailure || plan.Conflicts.Count > 0)
        {
            result.AddExecutionFailure(new ExecutionFailure(
                planItem.Id,
                "ManualReview",
                "执行阶段检测到冲突或人工复核要求。",
                true));
        }

        return result;
    }

    private static VerificationEvidence BuildEvidence(
        RewriteResult result,
        DecisionResultDto decisionResult,
        RunRewriteWorkflowRequest request)
    {
        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        bool success = result.ExecutionFailures.Count == 0;
        evidence.AddCompilationEvidence(new CompilationEvidence(
            success,
            success ? 0 : result.ExecutionFailures.Count,
            success ? "工作流编排验证通过。" : "工作流编排产生人工复核项。"));
        evidence.AddStaticReasoningEvidence(new StaticReasoningEvidence(
            request.TargetName,
            $"传播目标数：{request.PropagationTargets.Count}，保护项：{decisionResult.Protections.Count}。"));
        evidence.AddBehaviorEvidence(new BehaviorEvidence(
            request.PlanAction.ToString(),
            success,
            success ? "计划到结果闭环完成。" : "计划到结果闭环完成，但存在人工复核。"));
        evidence.UpdateRiskSummary(new RiskSummary(
            success ? "Low" : "Medium",
            !success,
            success
                ? new[] { "未发现额外阻断。" }
                : new[] { "存在保护项、冲突项或执行失败，需人工复核。" }));
        return evidence;
    }

    private static RunReport BuildReport(
        RunRewriteWorkflowRequest request,
        DecisionResultDto decisionResult,
        RewritePlan plan,
        RewriteResult result,
        VerificationEvidence evidence)
    {
        RunReport report = RunReport.Create(
            request.WorkspaceContextId,
            decisionResult.Decision.Id,
            plan.Id,
            result.Id,
            new ReportSummary(
                decisionResult.Decision.Approvals.Count,
                decisionResult.Decision.Rejections.Count,
                result.ExecutionFailures.Count,
                "传播、决策、执行、输出链路已由应用层串联。"),
            result.ExecutionFailures.Count == 0
                ? AuditConclusion.ApprovedForExecution
                : AuditConclusion.RequiresManualReview);
        report.AttachVerificationEvidence(evidence.Id);
        return report;
    }
}
