namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Dome.Analysis.Legacy;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// Legacy/Core-only composition root for Terraria runtime and shadow extraction paths.
/// Standard DomeApplication path must not depend on this factory.
/// </summary>
public static class TerrariaRuntimeLegacyFactory
{
    public static TerrariaRuntimeApplication CreateDefaultRuntimeApplication()
    {
        var progressReporter = new ConsoleTerrariaRuntimeProgressReporter();
        return new TerrariaRuntimeApplication(
            CreateLegacyDomeApplication(progressReporter),
            new TerrariaRuntimeEnvironmentBuilder(),
            new TerrariaRuntimeBuildExecutor(),
            new JsonRunReportStore(CreateArtifactWriter()),
            progressReporter,
            new TerrariaRuntimeLayoutFactory());
    }

    public static TerrariaRuntimeShadowExtractionApplication CreateDefaultShadowExtractionApplication()
    {
        var progressReporter = new ConsoleTerrariaRuntimeProgressReporter();
        return new TerrariaRuntimeShadowExtractionApplication(
            new ShadowExtractionInputResolver(CreateLegacyWorkspaceLoader()),
            new ShadowExtractionAnalysisStage(CreateLegacyAnalysisEngine()),
            new ShadowClosurePlanner(),
            new ShadowWorkspaceWriter(
                new TerrariaRuntimeShadowProjectBuilder(),
                new TerrariaRuntimeShadowSourceRewriter()),
            new TerrariaRuntimeBuildExecutor(),
            new ShadowExtractionReportBuilder(),
            new JsonShadowExtractionReportStore(CreateArtifactWriter()),
            progressReporter);
    }

    internal static DomeApplication CreateLegacyDomeApplication(IDomeProgressReporter progressReporter)
    {
        _ = progressReporter;
        return DomeApplicationFactory.CreateDefault();
    }

    internal static ApplicationAbstractions.IWorkspaceLoader CreateLegacyWorkspaceLoader() =>
        new TerrariaTools.Dome.Analysis.Roslyn.WorkspaceLoadCoordinator(
            new TerrariaTools.Dome.Analysis.Roslyn.CodeAnalysisWorkspaceLoader(),
            new TerrariaTools.Dome.Analysis.Roslyn.SourceOnlyLoader());

    internal static ApplicationAbstractions.IAnalysisEngine CreateLegacyAnalysisEngine() =>
        new LegacyAnalysisEngineFacade(new RoslynAnalysisEngine());

    internal static MarkingRuleEngine CreateMarkingRuleEngine() => new(MarkingRuleRegistry.CreateDefault());

    internal static ApplicationAbstractions.IRewriteExecutor CreateRewriteExecutor() => new RoslynRewriteExecutor();

    internal static RunReportBuilder CreateRunReportBuilder() => new();

    internal static ArtifactPlanBuilder CreateArtifactPlanBuilder() => new();

    internal static JsonArtifactWriter CreateArtifactWriter() => new();

}
