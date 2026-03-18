namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

internal sealed class ResolveInputStage(
    IShadowExtractionInputResolver inputResolver,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = await inputResolver.ResolveAsync(context.Request, progressReporter, cancellationToken);
        if (!inputResolution.IsSuccess || inputResolution.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Model.Primitives.FailureCode)inputResolution.FailureCode, context.OutputRootPath, inputResolution.Message);
            return;
        }

        context.SetInputResolution(inputResolution.Value);
    }
}

internal sealed class AnalyzeShadowStage(
    IShadowExtractionAnalysisStage analysisStage,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before shadow analysis.");
        var analysis = await analysisStage.AnalyzeAsync(inputResolution, progressReporter, cancellationToken);
        if (!analysis.IsSuccess || analysis.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Model.Primitives.FailureCode)analysis.FailureCode, inputResolution.Layout.OutputRootPath, analysis.Message);
            return;
        }

        context.SetAnalysis(analysis.Value);
    }
}

internal sealed class BuildClosureStage(
    IShadowClosurePlanner closurePlanner,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var analysis = context.Analysis ?? throw new InvalidOperationException("Analysis is required before closure planning.");
        var closurePlan = closurePlanner.BuildPlan(analysis, progressReporter, cancellationToken);
        if (!closurePlan.IsSuccess || closurePlan.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Model.Primitives.FailureCode)closurePlan.FailureCode, analysis.Input.Layout.OutputRootPath, closurePlan.Message);
            return Task.CompletedTask;
        }

        context.SetClosurePlan(closurePlan.Value);
        return Task.CompletedTask;
    }
}

internal sealed class WriteShadowWorkspaceStage(
    IShadowWorkspaceWriter workspaceWriter,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before workspace write.");
        var analysis = context.Analysis ?? throw new InvalidOperationException("Analysis is required before workspace write.");
        var closurePlan = context.ClosurePlan ?? throw new InvalidOperationException("Closure plan is required before workspace write.");
        var workspaceWrite = await workspaceWriter.WriteAsync(inputResolution, analysis, closurePlan, progressReporter, cancellationToken);
        if (!workspaceWrite.IsSuccess || workspaceWrite.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Model.Primitives.FailureCode)workspaceWrite.FailureCode, inputResolution.Layout.OutputRootPath, workspaceWrite.Message);
            return;
        }

        context.SetWorkspaceWriteResult(workspaceWrite.Value);
        progressReporter.Report($"[tr-shadow] Rewrite summary: preserved={workspaceWrite.Value.RewriteSummary.PreservedMembers}, defaulted={workspaceWrite.Value.RewriteSummary.DefaultedMembers}, emptied={workspaceWrite.Value.RewriteSummary.EmptiedMembers}");
    }
}

internal sealed class BuildShadowReportStage(IShadowExtractionReportBuilder reportBuilder) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        context.SetReport(reportBuilder.Build(
            context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before report build."),
            context.Analysis ?? throw new InvalidOperationException("Analysis is required before report build."),
            context.ClosurePlan ?? throw new InvalidOperationException("Closure plan is required before report build."),
            context.WorkspaceWriteResult ?? throw new InvalidOperationException("Workspace write result is required before report build.")));
        return Task.CompletedTask;
    }
}

internal sealed class BuildShadowWorkspaceStage(
    ITerrariaRuntimeBuildExecutor buildExecutor,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.InputResolution?.Layout ?? throw new InvalidOperationException("Input resolution is required before build.");
        progressReporter.Report("[tr-shadow] Building shadow workspace...");
        context.SetBuildSummary(await buildExecutor.ExecuteAsync(TerrariaRuntimeShadowStageHelpers.ToRuntimeLayout(layout), progressReporter, cancellationToken));
        context.UpdateReport((context.Report ?? throw new InvalidOperationException("Report is required before build update.")) with { TrBuildSummary = context.BuildSummary });
    }
}

internal sealed class PersistShadowReportStage(
    IShadowExtractionReportStore reportStore,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override async Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.InputResolution?.Layout ?? throw new InvalidOperationException("Input resolution is required before report persistence.");
        context.SetReportPath(Path.Combine(layout.ArtifactsPath, "shadow-report.json"));
        progressReporter.Report("[tr-shadow] Persisting shadow report...");
        await reportStore.SaveAsync(
            context.ReportPath,
            context.Report ?? throw new InvalidOperationException("Report is required before persistence."),
            cancellationToken);
    }
}

internal sealed class FinalizeShadowRunStage(ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<ShadowExtractionPipelineContext>
{
    public override Task ExecuteAsync(ShadowExtractionPipelineContext context, CancellationToken cancellationToken)
    {
        var inputResolution = context.InputResolution ?? throw new InvalidOperationException("Input resolution is required before finalization.");
        var buildSummary = context.BuildSummary ?? throw new InvalidOperationException("Build summary is required before finalization.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before finalization.");
        if (!buildSummary.BuildSucceeded)
        {
            progressReporter.Report($"[tr-shadow] Build failed with {TerrariaRuntimeShadowStageHelpers.CountBuildErrors(buildSummary.StandardOutput, buildSummary.StandardError)} reported errors.");
            SetFailure(context, TerrariaTools.Dome.Model.Primitives.FailureCode.BuildFailed, inputResolution.Layout.OutputRootPath, buildSummary.StandardError);
            return Task.CompletedTask;
        }

        progressReporter.Report("[tr-shadow] Shadow extraction pipeline completed.");
        context.TerminalState = new PipelineTerminalState(TerrariaTools.Dome.Application.Abstractions.RunResult.Success(inputResolution.Layout.OutputRootPath, reportPath));
        return Task.CompletedTask;
    }
}

internal static class TerrariaRuntimeShadowStageHelpers
{
    public static ApplicationAbstractions.TerrariaRuntimeLayout ToRuntimeLayout(ApplicationAbstractions.TerrariaRuntimeShadowLayout layout)
    {
        return new ApplicationAbstractions.TerrariaRuntimeLayout(
            layout.SolutionPath,
            layout.SourceRootPath,
            layout.OutputRootPath,
            layout.DependencyEnvironmentPath,
            layout.WorkspacePath,
            layout.ArtifactsPath,
            layout.WorkspaceSolutionPath);
    }

    public static int CountBuildErrors(string standardOutput, string standardError)
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
