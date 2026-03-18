namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

/// <summary>
/// Terraria Runtime application orchestration.
/// </summary>
public sealed class TerrariaRuntimeApplication
{
    private readonly IPipelineRunner<TerrariaRuntimePipelineContext> _pipelineRunner;

    public TerrariaRuntimeApplication(
        IDomeApplicationRunner domeApplication,
        ITerrariaRuntimeWorkspacePreparer workspacePreparer,
        ITerrariaRuntimeBuildExecutor buildExecutor,
        IRunReportStore runReportStore,
        ITerrariaRuntimeProgressReporter progressReporter,
        ITerrariaRuntimeLayoutFactory? layoutFactory = null)
    {
        var effectiveLayoutFactory = layoutFactory ?? new TerrariaRuntimeLayoutFactory();
        _pipelineRunner = new PipelineRunner<TerrariaRuntimePipelineContext>(
        [
            new CreateLayoutStage(effectiveLayoutFactory),
            new EnsureOutputDirectoriesStage(workspacePreparer),
            new RefreshDependencyEnvironmentStage(workspacePreparer, progressReporter),
            new RunDomeStage(domeApplication, progressReporter),
            new LoadReportStage(runReportStore),
            new PrepareWorkspaceStage(workspacePreparer, progressReporter),
            new BuildWorkspaceStage(buildExecutor, progressReporter),
            new PersistReportStage(runReportStore),
            new FinalizeRuntimeRunStage(progressReporter)
        ],
        new ProgressReporterPipelineObserver<TerrariaRuntimePipelineContext>(progressReporter.Report));
    }

    public Task<ApplicationAbstractions.RunResult> RunAsync(
        ApplicationAbstractions.TerrariaRuntimeRunRequest request,
        CancellationToken cancellationToken) =>
        RunApplicationAsync(request, cancellationToken);

    public async Task<ApplicationAbstractions.RunResult> RunApplicationAsync(ApplicationAbstractions.TerrariaRuntimeRunRequest request, CancellationToken cancellationToken)
    {
        var context = new TerrariaRuntimePipelineContext(request);
        await _pipelineRunner.RunAsync(context, cancellationToken);
        if (context.TerminalState == null)
        {
            throw new InvalidOperationException("Terraria runtime pipeline completed without producing a terminal result.");
        }

        return context.TerminalState.Result;
    }
}
