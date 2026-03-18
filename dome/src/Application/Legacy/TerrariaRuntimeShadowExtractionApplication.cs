namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

public sealed class TerrariaRuntimeShadowExtractionApplication
{
    private readonly IPipelineRunner<ShadowExtractionPipelineContext> _pipelineRunner;

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
        _pipelineRunner = new PipelineRunner<ShadowExtractionPipelineContext>(
        [
            new ResolveInputStage(inputResolver, progressReporter),
            new AnalyzeShadowStage(analysisStage, progressReporter),
            new BuildClosureStage(closurePlanner, progressReporter),
            new WriteShadowWorkspaceStage(workspaceWriter, progressReporter),
            new BuildShadowReportStage(reportBuilder),
            new BuildShadowWorkspaceStage(buildExecutor, progressReporter),
            new PersistShadowReportStage(reportStore, progressReporter),
            new FinalizeShadowRunStage(progressReporter)
        ],
        new ProgressReporterPipelineObserver<ShadowExtractionPipelineContext>(progressReporter.Report));
    }

    public Task<ApplicationAbstractions.RunResult> RunAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        CancellationToken cancellationToken) =>
        RunApplicationAsync(request, cancellationToken);

    public async Task<ApplicationAbstractions.RunResult> RunApplicationAsync(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request, CancellationToken cancellationToken)
    {
        var context = new ShadowExtractionPipelineContext(request);
        await _pipelineRunner.RunAsync(context, cancellationToken);
        if (context.TerminalState == null)
        {
            throw new InvalidOperationException("Shadow extraction pipeline completed without producing a terminal result.");
        }

        return context.TerminalState.Result;
    }
}
