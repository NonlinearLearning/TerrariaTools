namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// DomeApplication 工厂类，负责创建 DomeApplication 实例。
/// </summary>
public static class DomeApplicationFactory
{
    /// <summary>
    /// 创建默认的 DomeApplication 实例。
    /// </summary>
    /// <returns>配置了默认组件的 DomeApplication 实例。</returns>
    public static DomeApplication CreateDefault()
    {
        return new DomeApplication(
            new WorkspaceLoadCoordinator(
                new CodeAnalysisWorkspaceLoader(),
                new SourceOnlyLoader()),
            new RoslynAnalysisEngine(),
            new FunctionImpactAnalyzer(),
            new ReferenceZeroPredictionAnalyzer(),
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            new RoslynRewriteExecutor(),
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            new JsonArtifactWriter());
    }

    /// <summary>
    /// 创建默认的 Terraria Runtime 应用实例。
    /// </summary>
    /// <returns>配置了 TR 运行时组件的应用实例。</returns>
    public static TerrariaRuntimeApplication CreateDefaultTerrariaRuntimeApplication()
    {
        var progressReporter = new ConsoleTerrariaRuntimeProgressReporter();
        return new TerrariaRuntimeApplication(
            new DomeApplication(
                new WorkspaceLoadCoordinator(
                    new CodeAnalysisWorkspaceLoader(),
                    new SourceOnlyLoader()),
                new RoslynAnalysisEngine(),
                new FunctionImpactAnalyzer(),
                new ReferenceZeroPredictionAnalyzer(),
                new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
                new RoslynRewriteExecutor(),
                new RunReportBuilder(),
                new ArtifactPlanBuilder(),
                new JsonArtifactWriter(),
                progressReporter: progressReporter),
            new TerrariaRuntimeEnvironmentBuilder(),
            new TerrariaRuntimeBuildExecutor(),
            new JsonRunReportStore(new JsonArtifactWriter()),
            progressReporter,
            new TerrariaRuntimeLayoutFactory());
    }

    public static TerrariaRuntimeShadowExtractionApplication CreateDefaultTerrariaRuntimeShadowExtractionApplication()
    {
        var progressReporter = new ConsoleTerrariaRuntimeProgressReporter();
        return new TerrariaRuntimeShadowExtractionApplication(
            new ShadowExtractionInputResolver(
                new WorkspaceLoadCoordinator(
                    new CodeAnalysisWorkspaceLoader(),
                    new SourceOnlyLoader())),
            new ShadowExtractionAnalysisStage(new RoslynAnalysisEngine()),
            new ShadowClosurePlanner(),
            new ShadowWorkspaceWriter(
                new TerrariaRuntimeShadowProjectBuilder(),
                new TerrariaRuntimeShadowSourceRewriter()),
            new TerrariaRuntimeBuildExecutor(),
            new ShadowExtractionReportBuilder(),
            new JsonShadowExtractionReportStore(new JsonArtifactWriter()),
            progressReporter);
    }
}
