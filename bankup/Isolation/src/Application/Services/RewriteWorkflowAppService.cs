using Application.Abstractions;
using Application.Contracts;
using Application.Contracts.Decision;
using Application.Contracts.Propagation;
using Application.Contracts.Workflow;
using Application.Mappers;
using Domain.Analysis;
using Domain.Decision;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Analysis.Events;
using Logic.Decision;
using Logic.Marking.Events;
using Logic.Marking;
using Logic.Propagation;
using Logic.Propagation.Events;
using Logic.Rules;
using Logic.Workflow;
using Logic.Workflow.Events;

namespace Application.Services;

/// <summary>
/// 改写工作流应用服务实现。
/// </summary>
public sealed class RewriteWorkflowAppService : IRewriteWorkflowAppService
{
    private readonly IWorkspaceContextRepository workspaceContextRepository;
    private readonly IAnalysisSnapshotRepository analysisSnapshotRepository;
    private readonly IRuleTargetRepository ruleTargetRepository;
    private readonly IRewriteWorkflowMarkingPreparer rewriteWorkflowMarkingPreparer;
    private readonly IRewriteWorkflowRulePreset rewriteWorkflowRulePreset;
    private readonly IAnalysisDomainEventPublisher analysisDomainEventPublisher;
    private readonly IMarkingDomainEventPublisher markingDomainEventPublisher;
    private readonly IPropagationDomainEventPublisher propagationDomainEventPublisher;
    private readonly IRewriteWorkflowPropagationStage rewriteWorkflowPropagationStage;
    private readonly IRewriteWorkflowDecisionStage rewriteWorkflowDecisionStage;
    private readonly IRewriteWorkflowArtifactAssembler rewriteWorkflowArtifactAssembler;
    private readonly IDomainEventRecorder domainEventRecorder;

    public RewriteWorkflowAppService(
        IWorkspaceContextRepository workspaceContextRepository,
        IAnalysisSnapshotRepository analysisSnapshotRepository,
        IRuleTargetRepository ruleTargetRepository,
        IRewriteWorkflowMarkingPreparer rewriteWorkflowMarkingPreparer,
        IRewriteWorkflowRulePreset rewriteWorkflowRulePreset,
        IAnalysisDomainEventPublisher analysisDomainEventPublisher,
        IMarkingDomainEventPublisher markingDomainEventPublisher,
        IPropagationDomainEventPublisher propagationDomainEventPublisher,
        IRewriteWorkflowPropagationStage rewriteWorkflowPropagationStage,
        IRewriteWorkflowDecisionStage rewriteWorkflowDecisionStage,
        IRewriteWorkflowArtifactAssembler rewriteWorkflowArtifactAssembler,
        IDomainEventRecorder domainEventRecorder)
    {
        this.workspaceContextRepository = workspaceContextRepository;
        this.analysisSnapshotRepository = analysisSnapshotRepository;
        this.ruleTargetRepository = ruleTargetRepository;
        this.rewriteWorkflowMarkingPreparer = rewriteWorkflowMarkingPreparer;
        this.rewriteWorkflowRulePreset = rewriteWorkflowRulePreset;
        this.analysisDomainEventPublisher = analysisDomainEventPublisher;
        this.markingDomainEventPublisher = markingDomainEventPublisher;
        this.propagationDomainEventPublisher = propagationDomainEventPublisher;
        this.rewriteWorkflowPropagationStage = rewriteWorkflowPropagationStage;
        this.rewriteWorkflowDecisionStage = rewriteWorkflowDecisionStage;
        this.rewriteWorkflowArtifactAssembler = rewriteWorkflowArtifactAssembler;
        this.domainEventRecorder = domainEventRecorder;
    }


    public async Task<RewriteWorkflowRunDto> RunAsync(
        RunRewriteWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid runCorrelationId = request.RunCorrelationId != Guid.Empty
            ? request.RunCorrelationId
            : Guid.NewGuid();
        domainEventRecorder.Clear(runCorrelationId);
        WorkspaceContext workspaceContext = await GetWorkspaceAsync(request.WorkspaceContextId, cancellationToken);
        RuleTarget? ruleTarget = await GetRuleTargetAsync(request.RuleTargetId, cancellationToken);
        AnalysisCpgSnapshot? analysisSnapshot = await PublishAnalysisSnapshotAsync(
            request,
            runCorrelationId,
            workspaceContext,
            cancellationToken);
        ChangeCandidate? markingCandidate = await PublishMarkingCandidateAsync(
            ruleTarget,
            runCorrelationId,
            workspaceContext,
            cancellationToken);
        RuleCode workflowRuleCode = rewriteWorkflowRulePreset.ResolveMarkingRuleCode(request.RuleCode, ruleTarget?.RuleCode);
        PropagationResolution propagationResolution = rewriteWorkflowPropagationStage.Propagate(
            BuildPropagationStageInput(request, workflowRuleCode, analysisSnapshot, markingCandidate)).Resolution;
        PublishPropagation(
            runCorrelationId,
            analysisSnapshot?.WorkspaceContextId ?? request.WorkspaceContextId,
            propagationResolution);
        PropagationResultDto propagation = MapPropagation(runCorrelationId, propagationResolution);
        RewriteDecisionResolution decisionResolution = rewriteWorkflowDecisionStage.Decide(
            BuildDecisionStageInput(request, propagationResolution, rewriteWorkflowRulePreset)).Resolution;
        DecisionResultDto decisionResult = MapDecision(decisionResolution);
        RewriteDecision decision = decisionResolution.Decision;
        RewriteWorkflowArtifacts workflowArtifacts = rewriteWorkflowArtifactAssembler.Assemble(
            BuildWorkflowAssemblyInput(
                request,
                workflowRuleCode,
                runCorrelationId,
                workspaceContext,
                propagationResolution,
                decisionResolution));

        return new RewriteWorkflowRunDto
        {
            RunCorrelationId = runCorrelationId,
            Propagation = propagation,
            Candidate = propagation.Candidate,
            DecisionResult = decisionResult,
            Decision = decisionResult.Decision,
            Plan = ContractMapper.Map(workflowArtifacts.Plan),
            Result = ContractMapper.Map(workflowArtifacts.Result),
            Evidence = ContractMapper.Map(workflowArtifacts.Evidence),
            Report = ContractMapper.Map(workflowArtifacts.Report),
            DomainEvents = ContractMapper.Map(workflowArtifacts.DomainEvents),
        };
    }

    private async Task<AnalysisCpgSnapshot?> PublishAnalysisSnapshotAsync(
        RunRewriteWorkflowRequest request,
        Guid runCorrelationId,
        WorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        if (!request.AnalysisSnapshotId.HasValue)
        {
            return null;
        }

        AnalysisCpgSnapshot? snapshot = await analysisSnapshotRepository.GetCpgSnapshotAsync(
            request.AnalysisSnapshotId.Value,
            cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        analysisDomainEventPublisher.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
        });
        return snapshot;
    }

    private async Task<ChangeCandidate?> PublishMarkingCandidateAsync(
        RuleTarget? ruleTarget,
        Guid runCorrelationId,
        WorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ruleTarget is null)
        {
            return null;
        }

        IReadOnlyCollection<ChangeCandidate> markingCandidates = rewriteWorkflowMarkingPreparer.Prepare(ruleTarget);
        markingDomainEventPublisher.Publish(new MarkingDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContextId = workspaceContext.Id,
            RuleTarget = ruleTarget,
            Candidates = markingCandidates,
        });
        return markingCandidates.FirstOrDefault();
    }

    private async Task<RuleTarget?> GetRuleTargetAsync(Guid ruleTargetId, CancellationToken cancellationToken)
    {
        if (ruleTargetId == Guid.Empty)
        {
            return null;
        }

        return await ruleTargetRepository.GetAsync(ruleTargetId, cancellationToken);
    }

    private async Task<WorkspaceContext> GetWorkspaceAsync(Guid workspaceContextId, CancellationToken cancellationToken)
    {
        WorkspaceContext? workspaceContext = await workspaceContextRepository.GetAsync(workspaceContextId, cancellationToken);
        if (workspaceContext is null)
        {
            throw new InvalidOperationException($"未找到工作区上下文：{workspaceContextId}");
        }

        return workspaceContext;
    }

    private void PublishPropagation(
        Guid runCorrelationId,
        Guid workspaceContextId,
        PropagationResolution propagationResolution)
    {
        propagationDomainEventPublisher.Publish(new PropagationDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContextId = workspaceContextId,
            Resolution = propagationResolution,
        });
    }

    private static RewriteWorkflowPropagationStageInput BuildPropagationStageInput(
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

    private static RewriteWorkflowDecisionStageInput BuildDecisionStageInput(
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

    private static PropagationResultDto MapPropagation(Guid runCorrelationId, PropagationResolution propagation)
    {
        return new PropagationResultDto
        {
            RunCorrelationId = runCorrelationId,
            CandidateId = propagation.Candidate.Id,
            Candidate = ContractMapper.Map(propagation.Candidate),
            SliceBoundary = ContractMapper.Map(propagation.SliceBoundary),
            PropagationTraces = propagation.PropagationTraces.Select(ContractMapper.Map).ToArray(),
            FactReferences = propagation.FactReferences.Select(ContractMapper.Map).ToArray(),
        };
    }

    private static DecisionResultDto MapDecision(RewriteDecisionResolution decision)
    {
        RewriteDecisionDto decisionDto = ContractMapper.Map(decision.Decision);
        return new DecisionResultDto
        {
            CandidateId = decision.CandidateId,
            Decision = decisionDto,
            Approved = decision.Approved,
            Protections = decisionDto.Protections,
            Conflicts = decisionDto.Conflicts,
        };
    }

    private static RewriteWorkflowAssemblyInput BuildWorkflowAssemblyInput(
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
            TargetName = request.TargetName,
            DocumentPath = request.DocumentPath,
            MemberSignature = request.MemberSignature,
            AnchorText = request.AnchorText,
            PlanAction = ContractMapper.Map(request.PlanAction),
            PropagationTargets = request.PropagationTargets,
            SourceCode = request.SourceCode,
            ClassName = request.ClassName,
            MethodName = request.MethodName,
            ParameterCount = request.ParameterCount,
        };
    }
}
