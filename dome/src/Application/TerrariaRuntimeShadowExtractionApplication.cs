namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Core;

public sealed class TerrariaRuntimeShadowExtractionApplication
{
    private readonly IShadowExtractionInputResolver _inputResolver;
    private readonly IShadowExtractionAnalysisStage _analysisStage;
    private readonly IShadowClosurePlanner _closurePlanner;
    private readonly IShadowWorkspaceWriter _workspaceWriter;
    private readonly ITerrariaRuntimeBuildExecutor _buildExecutor;
    private readonly IShadowExtractionReportBuilder _reportBuilder;
    private readonly IShadowExtractionReportStore _reportStore;
    private readonly ITerrariaRuntimeProgressReporter _progressReporter;

    public TerrariaRuntimeShadowExtractionApplication(
        IShadowExtractionInputResolver inputResolver,
        IShadowExtractionAnalysisStage analysisStage,
        IShadowClosurePlanner closurePlanner,
        IShadowWorkspaceWriter workspaceWriter,
        ITerrariaRuntimeBuildExecutor buildExecutor,
        IShadowExtractionReportBuilder reportBuilder,
        IShadowExtractionReportStore reportStore,
        ITerrariaRuntimeProgressReporter progressReporter)
    {
        _inputResolver = inputResolver;
        _analysisStage = analysisStage;
        _closurePlanner = closurePlanner;
        _workspaceWriter = workspaceWriter;
        _buildExecutor = buildExecutor;
        _reportBuilder = reportBuilder;
        _reportStore = reportStore;
        _progressReporter = progressReporter;
    }

    public async Task<RunResult> RunAsync(TerrariaRuntimeShadowExtractionRequest request, CancellationToken cancellationToken)
    {
        var outputPath = TerrariaRuntimeShadowLayout.Create(request).OutputRootPath;

        var inputResolution = await _inputResolver.ResolveAsync(request, _progressReporter, cancellationToken);
        if (!inputResolution.IsSuccess || inputResolution.Value == null)
        {
            return RunResult.Failure(inputResolution.FailureCode, outputPath, inputResolution.Message);
        }

        var analysis = await _analysisStage.AnalyzeAsync(inputResolution.Value, _progressReporter, cancellationToken);
        if (!analysis.IsSuccess || analysis.Value == null)
        {
            return RunResult.Failure(analysis.FailureCode, inputResolution.Value.Layout.OutputRootPath, analysis.Message);
        }

        var closurePlan = _closurePlanner.BuildPlan(analysis.Value, _progressReporter, cancellationToken);
        if (!closurePlan.IsSuccess || closurePlan.Value == null)
        {
            return RunResult.Failure(closurePlan.FailureCode, inputResolution.Value.Layout.OutputRootPath, closurePlan.Message);
        }

        var workspaceWrite = await _workspaceWriter.WriteAsync(
            inputResolution.Value,
            analysis.Value,
            closurePlan.Value,
            _progressReporter,
            cancellationToken);
        if (!workspaceWrite.IsSuccess || workspaceWrite.Value == null)
        {
            return RunResult.Failure(workspaceWrite.FailureCode, inputResolution.Value.Layout.OutputRootPath, workspaceWrite.Message);
        }

        _progressReporter.Report($"[tr-shadow] Rewrite summary: preserved={workspaceWrite.Value.RewriteSummary.PreservedMembers}, defaulted={workspaceWrite.Value.RewriteSummary.DefaultedMembers}, emptied={workspaceWrite.Value.RewriteSummary.EmptiedMembers}");
        var report = _reportBuilder.Build(inputResolution.Value, analysis.Value, closurePlan.Value, workspaceWrite.Value);

        _progressReporter.Report("[tr-shadow] å¯®â‚¬æ¿®å¬¬ç´ªç’‡?shadow ç‘™ï½…å–…é‚è§„î”...");
        var buildSummary = await _buildExecutor.ExecuteAsync(ToRuntimeLayout(inputResolution.Value.Layout), _progressReporter, cancellationToken);
        report = report with { TrBuildSummary = buildSummary };

        var reportPath = Path.Combine(inputResolution.Value.Layout.ArtifactsPath, "shadow-report.json");
        _progressReporter.Report("[tr-shadow] å¯®â‚¬æ¿®å¬ªå•“é?shadow éŽ¶ãƒ¥æ†¡...");
        await _reportStore.SaveAsync(reportPath, report, cancellationToken);

        if (!buildSummary.BuildSucceeded)
        {
            _progressReporter.Report($"[tr-shadow] Build failed with {CountBuildErrors(buildSummary.StandardOutput, buildSummary.StandardError)} reported errors.");
            return RunResult.Failure(FailureCode.BuildFailed, inputResolution.Value.Layout.OutputRootPath, buildSummary.StandardError);
        }

        _progressReporter.Report("[tr-shadow] Shadow æ¤¤åœ­æ´°éŽ»æ„¬å½‡ç€¹å±¾åžšéŠ†?");
        return RunResult.Success(inputResolution.Value.Layout.OutputRootPath, reportPath);
    }

    private static TerrariaRuntimeLayout ToRuntimeLayout(TerrariaRuntimeShadowLayout layout)
    {
        return new TerrariaRuntimeLayout(
            layout.SolutionPath,
            layout.SourceRootPath,
            layout.OutputRootPath,
            layout.DependencyEnvironmentPath,
            layout.WorkspacePath,
            layout.ArtifactsPath,
            layout.WorkspaceSolutionPath);
    }

    private static int CountBuildErrors(string standardOutput, string standardError)
    {
        return CountOccurrences(standardOutput, ": error ") + CountOccurrences(standardError, ": error ");
    }

    private static int CountOccurrences(string text, string marker)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }
}
