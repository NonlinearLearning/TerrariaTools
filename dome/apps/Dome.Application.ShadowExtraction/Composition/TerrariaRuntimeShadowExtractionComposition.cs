namespace TerrariaTools.Dome.Application.Composition;

using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.ShadowExtraction.Host;
using TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

/// <summary>
/// 汇总影子提取流水线所需的依赖。
/// </summary>
/// <param name="InputResolver">输入解析器。</param>
/// <param name="AnalysisStage">分析阶段服务。</param>
/// <param name="ClosurePlanner">闭包规划器。</param>
/// <param name="WorkspaceWriter">工作区写入器。</param>
/// <param name="BuildExecutor">构建执行器。</param>
/// <param name="ReportBuilder">报告构建器。</param>
/// <param name="ReportStore">报告存储。</param>
/// <param name="ProgressReporter">进度上报器。</param>
internal sealed record ShadowExtractionPipelineDependencies(
    IShadowExtractionInputResolver InputResolver,
    IShadowExtractionAnalysisStage AnalysisStage,
    IShadowClosurePlanner ClosurePlanner,
    IShadowWorkspaceWriter WorkspaceWriter,
    ITerrariaRuntimeBuildExecutor BuildExecutor,
    IShadowExtractionReportBuilder ReportBuilder,
    IShadowExtractionReportStore ReportStore,
    ITerrariaRuntimeProgressReporter ProgressReporter);

/// <summary>
/// 提供影子提取应用的默认组合根。
/// </summary>
internal static class TerrariaRuntimeShadowExtractionCompositionRoot
{
    /// <summary>
    /// 创建使用默认依赖的影子提取应用实例。
    /// </summary>
    /// <returns>影子提取应用实例。</returns>
    public static TerrariaRuntimeShadowExtractionApplication CreateDefault()
    {
        var progressReporter = new ConsoleTerrariaRuntimeProgressReporter();
        return Create(
            new ShadowExtractionPipelineDependencies(
                new ShadowExtractionInputResolver(
                    ApplicationDefaultServices.CreateWorkspaceLoader(),
                    new TerrariaRuntimeShadowLayoutFactory()),
                new ShadowExtractionAnalysisStage(ApplicationDefaultServices.CreateAnalysisEngine()),
                new ShadowClosurePlanner(ApplicationDefaultServices.CreateSeedClosureAnalyzer()),
                new ShadowWorkspaceWriter(
                    new TerrariaRuntimeShadowProjectBuilder(),
                    new TerrariaRuntimeShadowSourceRewriter()),
                new TerrariaRuntimeBuildExecutor(),
                new ShadowExtractionReportBuilder(),
                new JsonShadowExtractionReportStore(ApplicationDefaultServices.CreateJsonArtifactWriter()),
                progressReporter));
    }

    /// <summary>
    /// 根据给定依赖创建影子提取应用实例。
    /// </summary>
    /// <param name="dependencies">影子提取组合根依赖集合。</param>
    /// <returns>影子提取应用实例。</returns>
    internal static TerrariaRuntimeShadowExtractionApplication Create(ShadowExtractionPipelineDependencies dependencies) =>
        new(
            new AssembledFlowRunner<ShadowExtractionPipelineContext>(
                _ =>
                {
                    var slots = TerrariaRuntimeShadowExtractionSlotAdapters.CreateDefaults(dependencies);
                    var recipe = TerrariaRuntimeShadowExtractionFlowRecipes.Standard(slots);
                    return TerrariaRuntimeShadowExtractionFlowRecipes.BuildStages(recipe);
                },
                new ProgressReporterPipelineObserver<ShadowExtractionPipelineContext>(dependencies.ProgressReporter.Report)));
}
