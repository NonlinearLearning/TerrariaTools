namespace TerrariaTools.Dome.Application.Composition;

using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Runtime.Host;

/// <summary>
/// 汇总运行时流水线所需的依赖。
/// </summary>
/// <param name="DomeApplication">标准 Dome 应用入口。</param>
/// <param name="WorkspacePreparer">运行时工作区准备器。</param>
/// <param name="BuildExecutor">运行时构建执行器。</param>
/// <param name="RunReportStore">运行报告存储。</param>
/// <param name="ProgressReporter">运行时进度上报器。</param>
/// <param name="LayoutFactory">可选的运行时布局工厂。</param>
internal sealed record TerrariaRuntimePipelineDependencies(
    IDomeApplicationRunner DomeApplication,
    ITerrariaRuntimeWorkspacePreparer WorkspacePreparer,
    ITerrariaRuntimeBuildExecutor BuildExecutor,
    IRunReportStore RunReportStore,
    ITerrariaRuntimeProgressReporter ProgressReporter,
    ITerrariaRuntimeLayoutFactory? LayoutFactory = null);

/// <summary>
/// 提供运行时应用的默认组合根。
/// </summary>
internal static class TerrariaRuntimeCompositionRoot
{
    /// <summary>
    /// 创建使用默认依赖的运行时应用实例。
    /// </summary>
    /// <returns>运行时应用实例。</returns>
    public static TerrariaRuntimeApplication CreateDefault()
    {
        var progressReporter = new ConsoleTerrariaRuntimeProgressReporter();
        return Create(
            new TerrariaRuntimePipelineDependencies(
                DomeApplicationFactory.CreateDefault(progressReporter),
                new TerrariaRuntimeEnvironmentBuilder(),
                new TerrariaRuntimeBuildExecutor(),
                new JsonRunReportStore(ApplicationDefaultServices.CreateJsonArtifactWriter()),
                progressReporter,
                new TerrariaRuntimeLayoutFactory()));
    }

    /// <summary>
    /// 根据给定依赖创建运行时应用实例。
    /// </summary>
    /// <param name="dependencies">运行时组合根依赖集合。</param>
    /// <returns>运行时应用实例。</returns>
    internal static TerrariaRuntimeApplication Create(TerrariaRuntimePipelineDependencies dependencies) =>
        new(
            new AssembledFlowRunner<TerrariaRuntimePipelineContext>(
                _ =>
                {
                    var slots = TerrariaRuntimeSlotAdapters.CreateDefaults(dependencies);
                    var recipe = TerrariaRuntimeFlowRecipes.Standard(slots);
                    return TerrariaRuntimeFlowRecipes.BuildStages(recipe);
                },
                new ProgressReporterPipelineObserver<TerrariaRuntimePipelineContext>(dependencies.ProgressReporter.Report)));
}

