namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// Dome 应用运行器接口。
/// </summary>
public interface IDomeApplicationRunner
{
    /// <summary>
    /// 执行 Dome 应用主流程。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    Task<ApplicationAbstractions.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Dome 应用默认实现。
/// </summary>
public sealed class DomeApplication : IDomeApplicationRunner
{
    private readonly IPipelineRunner<DomePipelineContext> _pipelineRunner;

    /// <summary>
    /// 使用外部流水线运行器初始化应用。
    /// </summary>
    /// <param name="pipelineRunner">流水线运行器。</param>
    public DomeApplication(IPipelineRunner<DomePipelineContext> pipelineRunner)
    {
        _pipelineRunner = pipelineRunner;
    }

    /// <summary>
    /// 使用完整依赖构建默认流水线并初始化应用。
    /// </summary>
    /// <param name="workspaceLoader">工作区加载器。</param>
    /// <param name="analysisEngine">分析引擎。</param>
    /// <param name="functionImpactAnalyzer">函数影响分析器。</param>
    /// <param name="referenceZeroPredictionAnalyzer">引用归零预测分析器。</param>
    /// <param name="markingRuleEngine">标记规则引擎。</param>
    /// <param name="rewriteExecutor">重写执行器。</param>
    /// <param name="runReportBuilder">运行报告构建器。</param>
    /// <param name="artifactPlanBuilder">产物计划构建器。</param>
    /// <param name="artifactWriter">产物写入器。</param>
    /// <param name="rewriteOutputStore">重写输出存储。</param>
    /// <param name="artifactEmissionService">产物发射服务。</param>
    /// <param name="progressReporter">进度上报器。</param>
    public DomeApplication(
        ApplicationAbstractions.IWorkspaceLoader workspaceLoader,
        ApplicationAbstractions.IAnalysisEngine analysisEngine,
        ApplicationAbstractions.IFunctionImpactAnalyzer functionImpactAnalyzer,
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
        MarkingRuleEngine markingRuleEngine,
        ApplicationAbstractions.IRewriteExecutor rewriteExecutor,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        ApplicationAbstractions.IArtifactWriter artifactWriter,
        IRewriteOutputStore? rewriteOutputStore = null,
        IArtifactEmissionService? artifactEmissionService = null,
        IDomeProgressReporter? progressReporter = null)
    {
        _pipelineRunner = CreatePipelineRunner(
            workspaceLoader,
            analysisEngine,
            functionImpactAnalyzer,
            referenceZeroPredictionAnalyzer,
            markingRuleEngine,
            rewriteExecutor,
            runReportBuilder,
            artifactPlanBuilder,
            artifactWriter,
            rewriteOutputStore,
            artifactEmissionService,
            progressReporter);
    }

    internal DomeApplication(
        WorkspaceLoadCoordinator workspaceLoader,
        RoslynAnalysisEngine analysisEngine,
        FunctionImpactAnalyzer functionImpactAnalyzer,
        ReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
        MarkingRuleEngine markingRuleEngine,
        ApplicationAbstractions.IRewriteExecutor rewriteExecutor,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        ApplicationAbstractions.IArtifactWriter artifactWriter,
        IRewriteOutputStore? rewriteOutputStore = null,
        IArtifactEmissionService? artifactEmissionService = null,
        IDomeProgressReporter? progressReporter = null)
    {
        // Legacy runtime/shadow-only entrypoint. Standard CreateDefault() must use the abstractions constructor.
        _pipelineRunner = CreatePipelineRunner(
            (ApplicationAbstractions.IWorkspaceLoader)workspaceLoader,
            (ApplicationAbstractions.IAnalysisEngine)analysisEngine,
            (ApplicationAbstractions.IFunctionImpactAnalyzer)functionImpactAnalyzer,
            (ApplicationAbstractions.IReferenceZeroPredictionAnalyzer)referenceZeroPredictionAnalyzer,
            markingRuleEngine,
            rewriteExecutor,
            runReportBuilder,
            artifactPlanBuilder,
            artifactWriter,
            rewriteOutputStore,
            artifactEmissionService,
            progressReporter);
    }

    /// <summary>
    /// 运行 Dome 流水线并返回终态结果。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<ApplicationAbstractions.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken)
    {
        var context = new DomePipelineContext(request);
        await _pipelineRunner.RunAsync(context, cancellationToken);
        if (context.TerminalState == null)
        {
            throw new InvalidOperationException("Dome pipeline completed without producing a terminal result.");
        }

        return context.TerminalState.Result;
    }

    private static IPipelineRunner<DomePipelineContext> CreatePipelineRunner(
        ApplicationAbstractions.IWorkspaceLoader workspaceLoader,
        ApplicationAbstractions.IAnalysisEngine analysisEngine,
        ApplicationAbstractions.IFunctionImpactAnalyzer functionImpactAnalyzer,
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer referenceZeroPredictionAnalyzer,
        MarkingRuleEngine markingRuleEngine,
        ApplicationAbstractions.IRewriteExecutor rewriteExecutor,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        ApplicationAbstractions.IArtifactWriter artifactWriter,
        IRewriteOutputStore? rewriteOutputStore,
        IArtifactEmissionService? artifactEmissionService,
        IDomeProgressReporter? progressReporter)
    {
        var effectiveRewriteOutputStore = rewriteOutputStore ?? new FileSystemRewriteOutputStore();
        var effectiveArtifactEmissionService = artifactEmissionService ?? new ArtifactEmissionService(artifactWriter);
        var effectiveProgressReporter = progressReporter ?? NullDomeProgressReporter.Instance;
        return new PipelineRunner<DomePipelineContext>(
        [
            new WorkspaceLoadStage(workspaceLoader, runReportBuilder, artifactPlanBuilder, effectiveArtifactEmissionService, effectiveProgressReporter),
            new AnalysisStage(analysisEngine, runReportBuilder, artifactPlanBuilder, effectiveArtifactEmissionService, effectiveProgressReporter),
            new AnalyzeOnlyFinalizeStage(runReportBuilder, artifactPlanBuilder, effectiveArtifactEmissionService),
            new MarkDecisionsStage(markingRuleEngine, referenceZeroPredictionAnalyzer, effectiveProgressReporter),
            new CompilePlanStage(functionImpactAnalyzer, artifactPlanBuilder, runReportBuilder, effectiveArtifactEmissionService, effectiveProgressReporter),
            new PlanOnlyFinalizeStage(runReportBuilder, artifactPlanBuilder, effectiveArtifactEmissionService),
            new RewriteStage(rewriteExecutor, effectiveRewriteOutputStore, runReportBuilder, artifactPlanBuilder, effectiveArtifactEmissionService, effectiveProgressReporter),
            new StandardFinalizeStage(runReportBuilder, artifactPlanBuilder, effectiveArtifactEmissionService, effectiveProgressReporter)
        ],
        new ProgressReporterPipelineObserver<DomePipelineContext>(effectiveProgressReporter.Report));
    }
}
