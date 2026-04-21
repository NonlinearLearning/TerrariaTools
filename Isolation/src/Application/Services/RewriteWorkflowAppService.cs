using Application.Abstractions;
using Application.Contracts.Workflow;
using Application.Services.RewriteWorkflow;
using Domain.Analysis;
using Domain.Marking;
using Domain.Workspaces;
using Logic.Analysis.Events;
using Logic.Marking;
using Logic.Marking.Events;
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
    private readonly RewriteWorkflowUseCase rewriteWorkflowUseCase;

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
        rewriteWorkflowUseCase = new RewriteWorkflowUseCase(
            workspaceContextRepository,
            analysisSnapshotRepository,
            ruleTargetRepository,
            rewriteWorkflowMarkingPreparer,
            rewriteWorkflowRulePreset,
            analysisDomainEventPublisher,
            markingDomainEventPublisher,
            propagationDomainEventPublisher,
            rewriteWorkflowPropagationStage,
            rewriteWorkflowDecisionStage,
            rewriteWorkflowArtifactAssembler,
            domainEventRecorder);
    }

    public async Task<RewriteWorkflowRunDto> RunAsync(
        RunRewriteWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        RewriteWorkflowRunState state = await rewriteWorkflowUseCase.RunAsync(request, cancellationToken);
        return RewriteWorkflowRunDtoAssembler.Map(state);
    }
}
