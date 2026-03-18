namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;

// These stages are legacy/Core-only runtime orchestration and are intentionally excluded from the standard DomeApplication path.

internal sealed class CreateLayoutStage(ITerrariaRuntimeLayoutFactory layoutFactory) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        context.SetLayout(layoutFactory.Create(context.Request));
        return Task.CompletedTask;
    }
}

internal sealed class EnsureOutputDirectoriesStage(ITerrariaRuntimeWorkspacePreparer workspacePreparer) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken) =>
        workspacePreparer.EnsureOutputDirectoriesAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before ensuring directories."),
            cancellationToken);
}

internal sealed class RefreshDependencyEnvironmentStage(
    ITerrariaRuntimeWorkspacePreparer workspacePreparer,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken) =>
        workspacePreparer.RefreshDependencyEnvironmentAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before refreshing dependencies."),
            progressReporter,
            cancellationToken);
}

internal sealed class RunDomeStage(
    IDomeApplicationRunner domeApplication,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before running dome.");
        progressReporter.Report("[tr-run] Running dome pipeline...");
        var runResult = await domeApplication.RunAsync(
            new ApplicationAbstractions.RunRequest(layout.SolutionPath, layout.ArtifactsPath, Array.Empty<string>(), TerrariaTools.Dome.Model.Primitives.RunMode.Standard),
            cancellationToken);
        context.SetReportPath(Path.Combine(layout.ArtifactsPath, "report.json"));
        if (!runResult.IsSuccess)
        {
            context.TerminalState = new PipelineTerminalState(
                ApplicationAbstractions.RunResult.Failure(
                    runResult.FailureCode,
                    runResult.OutputPath,
                    runResult.Message));
        }
    }
}

internal sealed class LoadReportStage(IRunReportStore runReportStore) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before report load.");
        var reportPath = context.ReportPath ?? Path.Combine(layout.ArtifactsPath, "report.json");
        var reportLoad = await runReportStore.LoadAsync(reportPath, cancellationToken);
        if (!reportLoad.IsSuccess || reportLoad.Value == null)
        {
            SetFailure(context, (TerrariaTools.Dome.Model.Primitives.FailureCode)reportLoad.FailureCode, layout.OutputRootPath, reportLoad.Message);
            return;
        }

        if (context.ReportPath == null)
        {
            context.SetReportPath(reportPath);
        }

        context.SetReport(reportLoad.Value);
    }
}

internal sealed class PrepareWorkspaceStage(
    ITerrariaRuntimeWorkspacePreparer workspacePreparer,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken) =>
        workspacePreparer.PrepareWorkspaceAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before workspace preparation."),
            progressReporter,
            cancellationToken);
}

internal sealed class BuildWorkspaceStage(
    ITerrariaRuntimeBuildExecutor buildExecutor,
    ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        context.SetBuildSummary(await buildExecutor.ExecuteAsync(
            context.Layout ?? throw new InvalidOperationException("Runtime layout is required before build."),
            progressReporter,
            cancellationToken));
    }
}

internal sealed class PersistReportStage(IRunReportStore runReportStore) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override async Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var report = context.Report ?? throw new InvalidOperationException("Run report is required before persistence.");
        var buildSummary = context.BuildSummary ?? throw new InvalidOperationException("Build summary is required before persistence.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before persistence.");
        context.UpdateReport(report with { TrBuildSummary = buildSummary });
        await runReportStore.SaveAsync(reportPath, context.Report, cancellationToken);
    }
}

internal sealed class FinalizeRuntimeRunStage(ITerrariaRuntimeProgressReporter progressReporter) : PipelineStage<TerrariaRuntimePipelineContext>
{
    public override Task ExecuteAsync(TerrariaRuntimePipelineContext context, CancellationToken cancellationToken)
    {
        var layout = context.Layout ?? throw new InvalidOperationException("Runtime layout is required before finalization.");
        var buildSummary = context.BuildSummary ?? throw new InvalidOperationException("Build summary is required before finalization.");
        var report = context.Report ?? throw new InvalidOperationException("Run report is required before finalization.");
        var reportPath = context.ReportPath ?? throw new InvalidOperationException("Report path is required before finalization.");
        if (!buildSummary.BuildSucceeded)
        {
            SetFailure(
                context,
                TerrariaTools.Dome.Model.Primitives.FailureCode.BuildFailed,
                layout.OutputRootPath,
                TerrariaRuntimeStageHelpers.BuildFailureMessage(buildSummary, report.AdvancedAnalysisSummary));
            return Task.CompletedTask;
        }

        progressReporter.Report("[tr-run] Runtime pipeline completed.");
        context.TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Success(layout.OutputRootPath, reportPath));
        return Task.CompletedTask;
    }
}

internal static class TerrariaRuntimeStageHelpers
{
    public static string BuildFailureMessage(
        ApplicationAbstractions.TerrariaRuntimeBuildSummary buildSummary,
        ModelAnalysis.AdvancedAnalysisSummary? advancedAnalysisSummary)
    {
        if (advancedAnalysisSummary == null)
        {
            return buildSummary.StandardError;
        }

        var notes = string.Join(", ", (advancedAnalysisSummary.Notes ?? Array.Empty<string>()).Take(3));
        var suffix = $"Advanced analysis: persistent types={advancedAnalysisSummary.PersistentTypeCount}, risky types={advancedAnalysisSummary.RiskyTypeCount}.";
        if (!string.IsNullOrEmpty(notes))
        {
            suffix += $" Notes: {notes}.";
        }

        return string.IsNullOrWhiteSpace(buildSummary.StandardError)
            ? suffix
            : $"{buildSummary.StandardError}{Environment.NewLine}{suffix}";
    }
}
