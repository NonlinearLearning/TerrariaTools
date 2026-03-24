namespace TerrariaTools.Dome.Application.Composition;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.Runtime.Host;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Application.UseCases.Runtime;

/// <summary>
/// Groups the active slot implementations for one runtime-family flow.
/// </summary>
internal sealed record TerrariaRuntimeFlowSlots(
    ITerrariaRuntimePrepareSlot Prepare,
    ITerrariaRuntimeExecuteDomeSlot ExecuteDome,
    ITerrariaRuntimeLoadReportSlot LoadReport,
    ITerrariaRuntimeBuildWorkspaceSlot BuildWorkspace,
    ITerrariaRuntimePersistSlot Persist);

/// <summary>
/// Immutable input for the runtime prepare slot.
/// </summary>
/// <param name="Request">The runtime request being prepared.</param>
internal sealed record TerrariaRuntimePrepareInput(ApplicationAbstractions.TerrariaRuntimeRunRequest Request);

/// <summary>
/// Immutable output for the runtime prepare slot.
/// </summary>
/// <param name="Request">The runtime request being prepared.</param>
/// <param name="Layout">The prepared runtime workspace layout.</param>
internal sealed record TerrariaRuntimePrepareOutput(
    ApplicationAbstractions.TerrariaRuntimeRunRequest Request,
    ApplicationAbstractions.TerrariaRuntimeLayout Layout);

/// <summary>
/// Immutable input for the runtime Dome-execution slot.
/// </summary>
/// <param name="Prepare">The prepared runtime workspace state.</param>
internal sealed record TerrariaRuntimeExecuteDomeInput(TerrariaRuntimePrepareOutput Prepare);

/// <summary>
/// Immutable output for the runtime Dome-execution slot.
/// </summary>
/// <param name="Prepare">The prepared runtime workspace state.</param>
/// <param name="ReportPath">The resolved report path.</param>
/// <param name="FailureResult">The failure result produced by Dome, if any.</param>
internal sealed record TerrariaRuntimeExecuteDomeOutput(
    TerrariaRuntimePrepareOutput Prepare,
    string ReportPath,
    ModelExecution.RunResult? FailureResult);

/// <summary>
/// Immutable input for the runtime report-loading slot.
/// </summary>
/// <param name="ExecuteDome">The runtime state after Dome execution.</param>
internal sealed record TerrariaRuntimeLoadReportInput(TerrariaRuntimeExecuteDomeOutput ExecuteDome);

/// <summary>
/// Immutable output for the runtime report-loading slot.
/// </summary>
/// <param name="ExecuteDome">The runtime state after Dome execution.</param>
/// <param name="Report">The loaded run report.</param>
internal sealed record TerrariaRuntimeLoadReportOutput(
    TerrariaRuntimeExecuteDomeOutput ExecuteDome,
    ModelExecution.RunReport Report);

/// <summary>
/// Immutable input for the runtime build-workspace slot.
/// </summary>
/// <param name="LoadReport">The runtime state after report loading.</param>
internal sealed record TerrariaRuntimeBuildWorkspaceInput(TerrariaRuntimeLoadReportOutput LoadReport);

/// <summary>
/// Immutable output for the runtime build-workspace slot.
/// </summary>
/// <param name="LoadReport">The runtime state after report loading.</param>
/// <param name="BuildSummary">The runtime workspace build summary.</param>
internal sealed record TerrariaRuntimeBuildWorkspaceOutput(
    TerrariaRuntimeLoadReportOutput LoadReport,
    ApplicationAbstractions.TerrariaRuntimeBuildSummary BuildSummary);

/// <summary>
/// Immutable input for the runtime persist slot.
/// </summary>
/// <param name="BuildWorkspace">The runtime state after workspace build.</param>
internal sealed record TerrariaRuntimePersistInput(TerrariaRuntimeBuildWorkspaceOutput BuildWorkspace);

/// <summary>
/// Defines the prepare slot contract for the runtime family.
/// </summary>
internal interface ITerrariaRuntimePrepareSlot
    : ApplicationAbstractions.IFlowSlot<TerrariaRuntimePrepareInput, TerrariaRuntimePrepareOutput>
{
}

/// <summary>
/// Defines the standard Dome delegation slot contract for the runtime family.
/// </summary>
internal interface ITerrariaRuntimeExecuteDomeSlot
    : ApplicationAbstractions.IFlowSlot<TerrariaRuntimeExecuteDomeInput, TerrariaRuntimeExecuteDomeOutput>
{
}

/// <summary>
/// Defines the report-loading slot contract for the runtime family.
/// </summary>
internal interface ITerrariaRuntimeLoadReportSlot
    : ApplicationAbstractions.IFlowSlot<TerrariaRuntimeLoadReportInput, StageResult<TerrariaRuntimeLoadReportOutput>>
{
}

/// <summary>
/// Defines the workspace-build slot contract for the runtime family.
/// </summary>
internal interface ITerrariaRuntimeBuildWorkspaceSlot
    : ApplicationAbstractions.IFlowSlot<TerrariaRuntimeBuildWorkspaceInput, TerrariaRuntimeBuildWorkspaceOutput>
{
}

/// <summary>
/// Defines the terminal persistence slot contract for the runtime family.
/// </summary>
internal interface ITerrariaRuntimePersistSlot
    : ApplicationAbstractions.IFlowSlot<TerrariaRuntimePersistInput, ModelExecution.RunResult>
{
}

/// <summary>
/// Creates the default runtime-family slot adapters from the current service graph.
/// </summary>
internal static class TerrariaRuntimeSlotAdapters
{
    public static TerrariaRuntimeFlowSlots CreateDefaults(TerrariaRuntimePipelineDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var effectiveLayoutFactory = dependencies.LayoutFactory ?? new TerrariaRuntimeLayoutFactory();
        return new TerrariaRuntimeFlowSlots(
            new TerrariaRuntimePrepareSlotAdapter(
                effectiveLayoutFactory,
                dependencies.WorkspacePreparer,
                dependencies.ProgressReporter),
            new TerrariaRuntimeExecuteDomeSlotAdapter(
                dependencies.DomeApplication,
                dependencies.ProgressReporter),
            new TerrariaRuntimeLoadReportSlotAdapter(dependencies.RunReportStore),
            new TerrariaRuntimeBuildWorkspaceSlotAdapter(
                dependencies.WorkspacePreparer,
                dependencies.BuildExecutor,
                dependencies.ProgressReporter),
            new TerrariaRuntimePersistSlotAdapter(
                dependencies.RunReportStore,
                dependencies.ProgressReporter));
    }
}

internal sealed class TerrariaRuntimePrepareSlotAdapter(
    ITerrariaRuntimeLayoutFactory layoutFactory,
    ITerrariaRuntimeWorkspacePreparer workspacePreparer,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimePrepareSlot
{
    public async Task<TerrariaRuntimePrepareOutput> ExecuteAsync(
        TerrariaRuntimePrepareInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var layout = layoutFactory.Create(input.Request);
        await workspacePreparer.EnsureOutputDirectoriesAsync(layout, cancellationToken);
        await workspacePreparer.RefreshDependencyEnvironmentAsync(layout, progressReporter, cancellationToken);
        return new TerrariaRuntimePrepareOutput(input.Request, layout);
    }
}

internal sealed class TerrariaRuntimeExecuteDomeSlotAdapter(
    IDomeApplicationRunner domeApplication,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeExecuteDomeSlot
{
    public async Task<TerrariaRuntimeExecuteDomeOutput> ExecuteAsync(
        TerrariaRuntimeExecuteDomeInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        progressReporter.Report("[tr-run] Running dome pipeline...");
        var layout = input.Prepare.Layout;
        var runResult = await domeApplication.RunAsync(
            new ApplicationAbstractions.RunRequest(
                layout.SolutionPath,
                layout.ArtifactsPath,
                Array.Empty<string>(),
                ApplicationAbstractions.RunMode.Standard),
            cancellationToken);
        var reportPath = runResult.ReportPath ?? Path.Combine(layout.ArtifactsPath, "report.json");
        return new TerrariaRuntimeExecuteDomeOutput(
            input.Prepare,
            reportPath,
            runResult.IsSuccess ? null : runResult);
    }
}

internal sealed class TerrariaRuntimeLoadReportSlotAdapter(IRunReportStore runReportStore) : ITerrariaRuntimeLoadReportSlot
{
    public async Task<StageResult<TerrariaRuntimeLoadReportOutput>> ExecuteAsync(
        TerrariaRuntimeLoadReportInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var reportLoad = await runReportStore.LoadAsync(input.ExecuteDome.ReportPath, cancellationToken);
        if (!reportLoad.IsSuccess || reportLoad.Value == null)
        {
            return StageResult<TerrariaRuntimeLoadReportOutput>.Failure(
                reportLoad.FailureCode,
                reportLoad.Message ?? "Runtime report load failed.");
        }

        return StageResult<TerrariaRuntimeLoadReportOutput>.Success(
            new TerrariaRuntimeLoadReportOutput(input.ExecuteDome, reportLoad.Value));
    }
}

internal sealed class TerrariaRuntimeBuildWorkspaceSlotAdapter(
    ITerrariaRuntimeWorkspacePreparer workspacePreparer,
    ITerrariaRuntimeBuildExecutor buildExecutor,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeBuildWorkspaceSlot
{
    public async Task<TerrariaRuntimeBuildWorkspaceOutput> ExecuteAsync(
        TerrariaRuntimeBuildWorkspaceInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var layout = input.LoadReport.ExecuteDome.Prepare.Layout;
        await workspacePreparer.PrepareWorkspaceAsync(layout, progressReporter, cancellationToken);
        var buildSummary = await buildExecutor.ExecuteAsync(layout, progressReporter, cancellationToken);
        return new TerrariaRuntimeBuildWorkspaceOutput(input.LoadReport, buildSummary);
    }
}

internal sealed class TerrariaRuntimePersistSlotAdapter(
    IRunReportStore runReportStore,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimePersistSlot
{
    public async Task<ModelExecution.RunResult> ExecuteAsync(
        TerrariaRuntimePersistInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var layout = input.BuildWorkspace.LoadReport.ExecuteDome.Prepare.Layout;
        var reportPath = input.BuildWorkspace.LoadReport.ExecuteDome.ReportPath;
        var report = input.BuildWorkspace.LoadReport.Report;
        await runReportStore.SaveAsync(reportPath, report, cancellationToken);

        if (!input.BuildWorkspace.BuildSummary.BuildSucceeded)
        {
            return ModelExecution.RunResult.Failure(
                ApplicationAbstractions.FailureCode.BuildFailed,
                layout.OutputRootPath,
                TerrariaRuntimeStageHelpers.BuildFailureMessage(
                    input.BuildWorkspace.BuildSummary,
                    report.AdvancedAnalysisSummary));
        }

        progressReporter.Report("[tr-run] Runtime pipeline completed.");
        return ModelExecution.RunResult.Success(layout.OutputRootPath, reportPath);
    }
}
