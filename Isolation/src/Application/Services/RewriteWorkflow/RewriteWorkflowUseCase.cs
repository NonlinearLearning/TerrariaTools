using Application.Contracts.Workflow;
using Domain.Analysis;
using Domain.Decision;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Analysis.Events;
using Logic.Decision;
using Logic.Marking;
using Logic.Marking.Events;
using Logic.Propagation;
using Logic.Propagation.Events;
using Logic.Rules;
using Logic.Workflow;
using Logic.Workflow.Events;

namespace Application.Services.RewriteWorkflow;

internal sealed class RewriteWorkflowUseCase
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

    public RewriteWorkflowUseCase(
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

    public async Task<RewriteWorkflowRunState> RunAsync(
        RunRewriteWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RewriteWorkflowRunContext runContext = await PrepareRunContextAsync(request, cancellationToken);
        PropagationResolution propagationResolution = rewriteWorkflowPropagationStage.Propagate(
            RewriteWorkflowStageInputFactory.BuildPropagation(
                request,
                runContext.WorkflowRuleCode,
                runContext.AnalysisSnapshot,
                runContext.MarkingCandidate)).Resolution;

        PublishPropagation(
            runContext.RunCorrelationId,
            runContext.PropagationWorkspaceContextId,
            propagationResolution);

        RewriteDecisionResolution decisionResolution = rewriteWorkflowDecisionStage.Decide(
            RewriteWorkflowStageInputFactory.BuildDecision(
                request,
                propagationResolution,
                rewriteWorkflowRulePreset)).Resolution;

        RewriteWorkflowArtifacts workflowArtifacts = rewriteWorkflowArtifactAssembler.Assemble(
            RewriteWorkflowStageInputFactory.BuildAssembly(
                request,
                runContext.WorkflowRuleCode,
                runContext.RunCorrelationId,
                runContext.WorkspaceContext,
                propagationResolution,
                decisionResolution));

        return new RewriteWorkflowRunState(
            request,
            runContext.RunCorrelationId,
            propagationResolution,
            decisionResolution,
            workflowArtifacts);
    }

    private async Task<RewriteWorkflowRunContext> PrepareRunContextAsync(
        RunRewriteWorkflowRequest request,
        CancellationToken cancellationToken)
    {
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

        RuleCode workflowRuleCode = rewriteWorkflowRulePreset.ResolveMarkingRuleCode(
            request.RuleCode,
            ruleTarget?.RuleCode);

        return new RewriteWorkflowRunContext(
            runCorrelationId,
            workspaceContext,
            analysisSnapshot,
            markingCandidate,
            workflowRuleCode);
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
        WorkspaceContext? workspaceContext = await workspaceContextRepository.GetAsync(
            workspaceContextId,
            cancellationToken);
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

    private readonly record struct RewriteWorkflowRunContext(
        Guid RunCorrelationId,
        WorkspaceContext WorkspaceContext,
        AnalysisCpgSnapshot? AnalysisSnapshot,
        ChangeCandidate? MarkingCandidate,
        RuleCode WorkflowRuleCode)
    {
        public Guid PropagationWorkspaceContextId => AnalysisSnapshot?.WorkspaceContextId ?? WorkspaceContext.Id;
    }
}
