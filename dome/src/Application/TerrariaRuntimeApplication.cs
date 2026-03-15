namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Core;

/// <summary>
/// Terraria Runtime application orchestration.
/// </summary>
public sealed class TerrariaRuntimeApplication
{
    private readonly IDomeApplicationRunner _domeApplication;
    private readonly ITerrariaRuntimeWorkspacePreparer _workspacePreparer;
    private readonly ITerrariaRuntimeBuildExecutor _buildExecutor;
    private readonly IRunReportStore _runReportStore;
    private readonly ITerrariaRuntimeProgressReporter _progressReporter;
    private readonly ITerrariaRuntimeLayoutFactory _layoutFactory;

    public TerrariaRuntimeApplication(
        IDomeApplicationRunner domeApplication,
        ITerrariaRuntimeWorkspacePreparer workspacePreparer,
        ITerrariaRuntimeBuildExecutor buildExecutor,
        IRunReportStore runReportStore,
        ITerrariaRuntimeProgressReporter progressReporter,
        ITerrariaRuntimeLayoutFactory? layoutFactory = null)
    {
        _domeApplication = domeApplication;
        _workspacePreparer = workspacePreparer;
        _buildExecutor = buildExecutor;
        _runReportStore = runReportStore;
        _progressReporter = progressReporter;
        _layoutFactory = layoutFactory ?? new TerrariaRuntimeLayoutFactory();
    }

    public async Task<RunResult> RunAsync(TerrariaRuntimeRunRequest request, CancellationToken cancellationToken)
    {
        var layout = _layoutFactory.Create(request);
        await _workspacePreparer.EnsureOutputDirectoriesAsync(layout, cancellationToken);

        await _workspacePreparer.RefreshDependencyEnvironmentAsync(layout, _progressReporter, cancellationToken);

        _progressReporter.Report("[tr-run] å¯®â‚¬æ¿®å¬«å¢½ç›?dome é’å—˜ç€½éŠ†ä½½î…¸é’æŽ‘æ‹°é€ç‘°å•“...");
        var runResult = await _domeApplication.RunAsync(
            new RunRequest(layout.SolutionPath, layout.ArtifactsPath, Array.Empty<string>(), RunMode.Standard),
            cancellationToken);

        var reportPath = Path.Combine(layout.ArtifactsPath, "report.json");
        if (!runResult.IsSuccess)
        {
            return runResult;
        }

        var reportLoad = await _runReportStore.LoadAsync(reportPath, cancellationToken);
        if (!reportLoad.IsSuccess || reportLoad.Value == null)
        {
            return RunResult.Failure(reportLoad.FailureCode, layout.OutputRootPath, reportLoad.Message);
        }

        await _workspacePreparer.PrepareWorkspaceAsync(layout, _progressReporter, cancellationToken);

        var buildSummary = await _buildExecutor.ExecuteAsync(layout, _progressReporter, cancellationToken);
        var report = reportLoad.Value with { TrBuildSummary = buildSummary };
        await _runReportStore.SaveAsync(reportPath, report, cancellationToken);

        if (!buildSummary.BuildSucceeded)
        {
            return RunResult.Failure(FailureCode.BuildFailed, layout.OutputRootPath, BuildFailureMessage(buildSummary, report.AdvancedAnalysisSummary));
        }

        _progressReporter.Report("[tr-run] TR æ¶“æ’¶æ•¤æ©æ„¯î”‘å¨´ä½ºâ–¼å®¸å‰åžšé”ç†·ç•¬éŽ´æ„©â‚¬?");
        return RunResult.Success(layout.OutputRootPath, reportPath);
    }

    private static string BuildFailureMessage(
        TerrariaRuntimeBuildSummary buildSummary,
        AdvancedAnalysisSummary? advancedAnalysisSummary)
    {
        if (advancedAnalysisSummary == null)
        {
            return buildSummary.StandardError;
        }

        var methodRoots = string.Join(", ", advancedAnalysisSummary.MethodRoots.Take(3).Select(static item => item.Value));
        var symbolRoots = string.Join(", ", advancedAnalysisSummary.SymbolRoots.Take(3));
        var suffix = $"Advanced analysis: method roots={advancedAnalysisSummary.MethodRootCount}, symbol roots={advancedAnalysisSummary.SymbolRootCount}, method SCC={advancedAnalysisSummary.MethodSccCount}, symbol SCC={advancedAnalysisSummary.SymbolSccCount}.";
        if (!string.IsNullOrEmpty(methodRoots))
        {
            suffix += $" Sample method roots: {methodRoots}.";
        }

        if (!string.IsNullOrEmpty(symbolRoots))
        {
            suffix += $" Sample symbol roots: {symbolRoots}.";
        }

        return string.IsNullOrWhiteSpace(buildSummary.StandardError)
            ? suffix
            : $"{buildSummary.StandardError}{Environment.NewLine}{suffix}";
    }
}
