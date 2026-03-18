namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Analysis.Legacy;

// Shadow extraction stages remain legacy orchestration and must not be referenced by the standard DomeApplication path.

public sealed class ShadowExtractionInputResolver(ApplicationAbstractions.IWorkspaceLoader workspaceLoader) : IShadowExtractionInputResolver
{
    public async Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var layout = ApplicationAbstractions.TerrariaRuntimeShadowLayout.Create(request);
        Directory.CreateDirectory(layout.OutputRootPath);
        Directory.CreateDirectory(layout.ArtifactsPath);

        progressReporter.Report($"[tr-shadow] Starting workspace load: {request.SolutionPath}");
        var loadResult = await workspaceLoader.LoadAsync(request.SolutionPath, ApplicationAbstractions.WorkspaceLoadOptions.Default, cancellationToken);
        if (!ShadowExtractionLegacySupport.HasValidLoadResult(loadResult))
        {
            var message = loadResult.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found.";
            return StageResult<ShadowExtractionInputResolution>.Failure(ModelPrimitives.FailureCode.WorkspaceLoadFailed, message);
        }

        progressReporter.Report($"[tr-shadow] Workspace load completed with {loadResult.Documents.Count} C# documents.");
        return StageResult<ShadowExtractionInputResolution>.Success(new ShadowExtractionInputResolution(request, layout, loadResult));
    }
}

public sealed class ShadowExtractionAnalysisStage(ApplicationAbstractions.IAnalysisEngine analysisEngine) : IShadowExtractionAnalysisStage
{
    public async Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter.Report("[tr-shadow] Starting Roslyn analysis...");

        try
        {
            var analysis = await ShadowExtractionLegacySupport.AnalyzeAsync(analysisEngine, input, cancellationToken);
            progressReporter.Report($"[tr-shadow] Seed member resolved: {analysis.SeedNode.MemberId.Value}");
            return StageResult<ShadowExtractionAnalysis>.Success(analysis);
        }
        catch (InvalidOperationException ex)
        {
            return StageResult<ShadowExtractionAnalysis>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, ex.Message);
        }
    }
}

public sealed class ShadowClosurePlanner : IShadowClosurePlanner
{
    public StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        return ShadowExtractionLegacySupport.BuildClosurePlan(analysis, progressReporter, cancellationToken);
    }
}

public sealed class ShadowWorkspaceWriter(
    TerrariaRuntimeShadowProjectBuilder shadowProjectBuilder,
    TerrariaRuntimeShadowSourceRewriter shadowSourceRewriter) : IShadowWorkspaceWriter
{
    public Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        return ShadowExtractionLegacySupport.WriteWorkspaceAsync(
            shadowProjectBuilder,
            shadowSourceRewriter,
            input,
            analysis,
            closurePlan,
            progressReporter,
            cancellationToken);
    }
}

public sealed class ShadowExtractionReportBuilder : IShadowExtractionReportBuilder
{
    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        return new ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport(
            input.Request.SeedMemberName,
            analysis.SeedNode.MemberId.Value,
            closurePlan.IncludedDocuments,
            closurePlan.ReachableMethods.Select(static memberId => memberId.Value).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            analysis.AnalysisContext.AdvancedAnalysis.BuildSummary(),
            workspaceWriteResult.RewrittenDocuments.Count,
            workspaceWriteResult.RewriteSummary);
    }
}
