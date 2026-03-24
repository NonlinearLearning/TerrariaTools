namespace TerrariaTools.Dome.Application.Composition;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Core.Rules.Services;

/// <summary>
/// 汇总构建标准 Dome 流水线所需的依赖。
/// </summary>
/// <param name="WorkspaceLoader">工作区加载器。</param>
/// <param name="AnalysisEngine">分析引擎。</param>
/// <param name="FunctionImpactAnalyzer">函数影响分析器。</param>
/// <param name="ReferenceZeroPredictionAnalyzer">引用归零预测分析器。</param>
/// <param name="MarkingRuleEngine">标记规则引擎。</param>
/// <param name="RewriteExecutor">重写执行器。</param>
/// <param name="RunReportBuilder">运行报告构建器。</param>
/// <param name="ArtifactPlanBuilder">产物计划构建器。</param>
/// <param name="ArtifactWriter">产物写入器。</param>
/// <param name="RewriteOutputStore">可选的重写输出存储。</param>
/// <param name="ArtifactEmissionService">可选的产物输出服务。</param>
internal sealed record DomePipelineDependencies(
    ApplicationAbstractions.IWorkspaceLoader WorkspaceLoader,
    ApplicationAbstractions.IAnalysisEngine AnalysisEngine,
    ApplicationAbstractions.IFunctionImpactAnalyzer FunctionImpactAnalyzer,
    ApplicationAbstractions.IReferenceZeroPredictionAnalyzer ReferenceZeroPredictionAnalyzer,
    MarkingRuleEngine MarkingRuleEngine,
    ApplicationAbstractions.IRewriteExecutor RewriteExecutor,
    RunReportBuilder RunReportBuilder,
    ArtifactPlanBuilder ArtifactPlanBuilder,
    ApplicationAbstractions.IArtifactWriter ArtifactWriter,
    IRewriteOutputStore? RewriteOutputStore = null,
    IArtifactEmissionService? ArtifactEmissionService = null);

/// <summary>
/// 提供标准 Dome 应用的默认组合根。
/// </summary>
internal static class DomeApplicationCompositionRoot
{
    /// <summary>
    /// 创建使用默认依赖的 Dome 应用实例。
    /// </summary>
    /// <param name="progressReporter">可选的进度上报器。</param>
    /// <returns>Dome 应用实例。</returns>
    public static DomeApplication CreateDefault(IDomeProgressReporter? progressReporter = null)
    {
        return Create(
            new DomePipelineDependencies(
                ApplicationDefaultServices.CreateWorkspaceLoader(),
                ApplicationDefaultServices.CreateAnalysisEngine(),
                ApplicationDefaultServices.CreateFunctionImpactAnalyzer(),
                ApplicationDefaultServices.CreateReferenceZeroPredictionAnalyzer(),
                ApplicationDefaultServices.CreateMarkingRuleEngine(),
                ApplicationDefaultServices.CreateRewriteExecutor(),
                ApplicationDefaultServices.CreateRunReportBuilder(),
                ApplicationDefaultServices.CreateArtifactPlanBuilder(),
                ApplicationDefaultServices.CreateArtifactWriter()),
            progressReporter);
    }

    /// <summary>
    /// 根据给定依赖创建 Dome 应用实例。
    /// </summary>
    /// <param name="dependencies">组合根依赖集合。</param>
    /// <param name="progressReporter">可选的进度上报器。</param>
    /// <returns>Dome 应用实例。</returns>
    internal static DomeApplication Create(
        DomePipelineDependencies dependencies,
        IDomeProgressReporter? progressReporter = null)
    {
        var effectiveRewriteOutputStore = dependencies.RewriteOutputStore ?? new FileSystemRewriteOutputStore();
        var effectiveArtifactEmissionService = dependencies.ArtifactEmissionService ?? new ArtifactEmissionService(dependencies.ArtifactWriter);
        var effectiveProgressReporter = progressReporter ?? NullDomeProgressReporter.Instance;
        var effectiveDependencies = dependencies with
        {
            RewriteOutputStore = effectiveRewriteOutputStore,
            ArtifactEmissionService = effectiveArtifactEmissionService
        };

        return new DomeApplication(
            new AssembledFlowRunner<DomePipelineContext>(
                context =>
                {
                    var slots = DomeSlotAdapters.CreateDefaults(
                        effectiveDependencies,
                        effectiveRewriteOutputStore,
                        effectiveArtifactEmissionService,
                        effectiveProgressReporter);
                    var recipe = DomeFlowRecipes.ForMode(context.Request.Mode, slots);
                    return DomeFlowRecipes.BuildStages(
                        recipe,
                        effectiveDependencies.RunReportBuilder,
                        effectiveDependencies.ArtifactPlanBuilder,
                        effectiveArtifactEmissionService,
                        effectiveProgressReporter);
                },
                new ProgressReporterPipelineObserver<DomePipelineContext>(effectiveProgressReporter.Report)));
    }
}
