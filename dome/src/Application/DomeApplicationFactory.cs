namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

/// <summary>
/// Standard DomeApplication composition root.
/// Runtime and shadow extraction legacy/Core composition is isolated outside the standard Application project.
/// </summary>
public static class DomeApplicationFactory
{
    public static DomeApplication CreateDefault()
    {
        ApplicationAbstractions.IWorkspaceLoader workspaceLoader =
            new WorkspaceLoadCoordinator(
                new CodeAnalysisWorkspaceLoader(),
                new SourceOnlyLoader());
        ApplicationAbstractions.IAnalysisEngine analysisEngine =
            (ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine();
        ApplicationAbstractions.IFunctionImpactAnalyzer functionImpactAnalyzer =
            (ApplicationAbstractions.IFunctionImpactAnalyzer)new FunctionImpactAnalyzer();
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer predictionAnalyzer =
            (ApplicationAbstractions.IReferenceZeroPredictionAnalyzer)new ReferenceZeroPredictionAnalyzer();
        ApplicationAbstractions.IRewriteExecutor rewriteExecutor =
            new RoslynRewriteExecutor();
        ApplicationAbstractions.IArtifactWriter artifactWriter =
            new JsonArtifactWriter();

        return new DomeApplication(
            workspaceLoader,
            analysisEngine,
            functionImpactAnalyzer,
            predictionAnalyzer,
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            rewriteExecutor,
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            artifactWriter);
    }
}
