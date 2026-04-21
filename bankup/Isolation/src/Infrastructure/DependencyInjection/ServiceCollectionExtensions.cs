using Domain.Analysis;
using Domain.Marking;
using Domain.Rewrite;
using Domain.Workspaces;
using Infrastructure.Persistence;
using Infrastructure.Roslyn;
using Logic.Analysis;
using Logic.Analysis.Events;
using Logic.Decision;
using Logic.Marking;
using Logic.Marking.Events;
using Logic.Propagation;
using Logic.Propagation.Events;
using Logic.Rules;
using Logic.Rewrite;
using Logic.Workflow;
using Logic.Workflow.Events;
using Logic.Workspaces;
using Microsoft.Extensions.DependencyInjection;

using Infrastructure.Analysis;

namespace Infrastructure.DependencyInjection;

/// <summary>
/// 注册 Infrastructure、Domain 仓储与 Logic 支撑服务。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Isolation 基础分析能力。
    /// </summary>
    public static IServiceCollection AddIsolationAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkspaceContextRepository, InMemoryWorkspaceContextRepository>();
        services.AddSingleton<IAnalysisSnapshotRepository, InMemoryAnalysisSnapshotRepository>();
        services.AddSingleton<IRuleTargetRepository, InMemoryRuleTargetRepository>();
        services.AddSingleton<IRuleCatalog, RuleCatalog>();
        services.AddSingleton<IEnabledRuleFactory, EnabledRuleFactory>();
        services.AddSingleton<IMarkingRulePreset, MarkingRulePreset>();
        services.AddSingleton<IRulePresetProvider, WorkspaceDefaultRulePreset>();
        services.AddSingleton<IPropagationRulePreset, PropagationRulePreset>();
        services.AddSingleton<IRewriteWorkflowRulePreset, RewriteWorkflowRulePreset>();
        services.AddSingleton<IWorkspaceContextBuilder, WorkspaceContextBuilder>();
        services.AddSingleton<IWorkspaceRuleDefaultsBuilder, WorkspaceRuleDefaultsBuilder>();
        services.AddSingleton<IRuleTargetBuilder, RuleTargetBuilder>();
        services.AddSingleton<IChangeCandidateMarker, ChangeCandidateMarker>();
        services.AddSingleton<IRuleTargetCandidateBuilder, RuleTargetCandidateBuilder>();
        services.AddSingleton<IRuleTargetMarkingPreparer, RuleTargetMarkingPreparer>();
        services.AddSingleton<IAnalysisSnapshotComposer, AnalysisSnapshotComposer>();
        services.AddSingleton<AnalysisEventSequenceBuilder>();
        services.AddSingleton<IAnalysisDomainEventPublisher, AnalysisDomainEventPublisher>();
        services.AddSingleton<IAnalysisInputDescriptorBuilder, AnalysisInputDescriptorBuilder>();
        services.AddSingleton<IAnalysisCpgSnapshotAssembler, AnalysisCpgSnapshotAssembler>();
        services.AddSingleton<MarkingEventSequenceBuilder>();
        services.AddSingleton<IMarkingDomainEventPublisher, MarkingDomainEventPublisher>();
        services.AddSingleton<IRewriteDecisionAssessmentBuilder, RewriteDecisionAssessmentBuilder>();
        services.AddSingleton<IRewriteDecisionMaker, RewriteDecisionMaker>();
        services.AddSingleton<IImpactPropagator, ImpactPropagator>();
        services.AddSingleton<PropagationEventSequenceBuilder>();
        services.AddSingleton<IPropagationDomainEventPublisher, PropagationDomainEventPublisher>();
        services.AddSingleton<IRewritePlanCompiler, RewritePlanCompiler>();
        services.AddSingleton<IRewritePlanExecutor, RewritePlanExecutor>();
        services.AddSingleton<ICompilationEvidenceCollector, CompilationEvidenceCollector>();
        services.AddSingleton<IStaticReasoningEvidenceCollector, StaticReasoningEvidenceCollector>();
        services.AddSingleton<IBehaviorEvidenceCollector, BehaviorEvidenceCollector>();
        services.AddSingleton<IRunReportAssembler, RunReportAssembler>();
        services.AddSingleton<IDomainEventRecorder, InMemoryDomainEventRecorder>();
        services.AddSingleton<WorkflowEventSequenceBuilder>();
        services.AddSingleton<IRewriteWorkflowPlanStage, RewriteWorkflowPlanStage>();
        services.AddSingleton<IRewriteWorkflowExecutionStage, RewriteWorkflowExecutionStage>();
        services.AddSingleton<IRewriteWorkflowEvidenceStage, RewriteWorkflowEvidenceStage>();
        services.AddSingleton<IRewriteWorkflowReportStage, RewriteWorkflowReportStage>();
        services.AddSingleton<IRewriteWorkflowEventStage, RewriteWorkflowEventStage>();
        services.AddSingleton<IRewriteWorkflowArtifactAssembler, RewriteWorkflowArtifactAssembler>();
        services.AddSingleton<IAnalysisSnapshotFactory, DefaultAnalysisSnapshotBuilder>();
        services.AddSingleton<IAnalysisCpgGateway, AnalysisBackedCpgGateway>();
        services.AddSingleton<ICodeIsolationGateway, RoslynCodeIsolationGateway>();
        services.AddSingleton<IRoslynCodeIsolationFacade, RoslynCodeIsolationFacade>();
        return services;
    }
}
