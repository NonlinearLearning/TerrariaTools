namespace TerrariaTools.Dome.Application.Composition;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

/// <summary>
/// Groups the active slot implementations for one shadow-extraction flow.
/// </summary>
internal sealed record TerrariaRuntimeShadowExtractionFlowSlots(
    ITerrariaRuntimeShadowResolveInputSlot ResolveInput,
    ITerrariaRuntimeShadowAnalyzeSlot Analyze,
    ITerrariaRuntimeShadowBuildClosureSlot BuildClosure,
    ITerrariaRuntimeShadowWriteWorkspaceSlot WriteWorkspace,
    ITerrariaRuntimeShadowBuildSlot Build,
    ITerrariaRuntimeShadowPersistSlot Persist);

/// <summary>
/// Immutable input for the shadow resolve-input slot.
/// </summary>
/// <param name="Request">The requested shadow extraction run.</param>
internal sealed record ShadowResolveInputInput(
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest Request);

/// <summary>
/// Immutable output for the shadow resolve-input slot.
/// </summary>
/// <param name="InputResolution">The resolved workspace input state, if resolution succeeded.</param>
/// <param name="FailureCode">The failure code produced by the slot, if any.</param>
/// <param name="Message">The failure message produced by the slot, if any.</param>
internal sealed record ShadowResolveInputOutput(
    ShadowExtractionInputResolution? InputResolution,
    ApplicationAbstractions.FailureCode? FailureCode = null,
    string? Message = null);

/// <summary>
/// Immutable input for the shadow analyze slot.
/// </summary>
/// <param name="ResolveInput">The resolved input state.</param>
internal sealed record ShadowAnalyzeInput(ShadowResolveInputOutput ResolveInput);

/// <summary>
/// Immutable output for the shadow analyze slot.
/// </summary>
/// <param name="Analysis">The completed analysis, if analysis succeeded.</param>
/// <param name="FailureCode">The failure code produced by the slot, if any.</param>
/// <param name="Message">The failure message produced by the slot, if any.</param>
internal sealed record ShadowAnalyzeOutput(
    ShadowExtractionAnalysis? Analysis,
    ApplicationAbstractions.FailureCode? FailureCode = null,
    string? Message = null);

/// <summary>
/// Immutable input for the shadow closure-planning slot.
/// </summary>
/// <param name="Analyze">The completed analysis state.</param>
internal sealed record ShadowBuildClosureInput(ShadowAnalyzeOutput Analyze);

/// <summary>
/// Immutable output for the shadow closure-planning slot.
/// </summary>
/// <param name="Analysis">The analysis used to produce the closure plan.</param>
/// <param name="ClosurePlan">The closure plan, if planning succeeded.</param>
/// <param name="FailureCode">The failure code produced by the slot, if any.</param>
/// <param name="Message">The failure message produced by the slot, if any.</param>
internal sealed record ShadowBuildClosureOutput(
    ShadowExtractionAnalysis? Analysis,
    ShadowClosurePlan? ClosurePlan,
    ApplicationAbstractions.FailureCode? FailureCode = null,
    string? Message = null);

/// <summary>
/// Immutable input for the shadow workspace-write slot.
/// </summary>
/// <param name="BuildClosure">The planned closure state.</param>
internal sealed record ShadowWriteWorkspaceInput(ShadowBuildClosureOutput BuildClosure);

/// <summary>
/// Immutable output for the shadow workspace-write slot.
/// </summary>
/// <param name="BuildClosure">The closure state used by the slot.</param>
/// <param name="WorkspaceWriteResult">The workspace write result, if writing succeeded.</param>
/// <param name="FailureCode">The failure code produced by the slot, if any.</param>
/// <param name="Message">The failure message produced by the slot, if any.</param>
internal sealed record ShadowWriteWorkspaceOutput(
    ShadowBuildClosureOutput BuildClosure,
    ShadowWorkspaceWriteResult? WorkspaceWriteResult,
    ApplicationAbstractions.FailureCode? FailureCode = null,
    string? Message = null);

/// <summary>
/// Immutable input for the shadow build slot.
/// </summary>
/// <param name="WriteWorkspace">The written workspace state.</param>
internal sealed record ShadowBuildInput(ShadowWriteWorkspaceOutput WriteWorkspace);

/// <summary>
/// Immutable output for the shadow build slot.
/// </summary>
/// <param name="WriteWorkspace">The written workspace state.</param>
/// <param name="Report">The report enriched with build results.</param>
/// <param name="BuildSummary">The workspace build summary.</param>
internal sealed record ShadowBuildOutput(
    ShadowWriteWorkspaceOutput WriteWorkspace,
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Report,
    ApplicationAbstractions.TerrariaRuntimeBuildSummary BuildSummary);

/// <summary>
/// Immutable input for the shadow persist slot.
/// </summary>
/// <param name="Build">The built workspace state.</param>
internal sealed record ShadowPersistInput(ShadowBuildOutput Build);

/// <summary>
/// Defines the resolve-input slot contract for the shadow-extraction flow.
/// </summary>
internal interface ITerrariaRuntimeShadowResolveInputSlot
    : ApplicationAbstractions.IFlowSlot<ShadowResolveInputInput, ShadowResolveInputOutput>
{
}

/// <summary>
/// Defines the analysis slot contract for the shadow-extraction flow.
/// </summary>
internal interface ITerrariaRuntimeShadowAnalyzeSlot
    : ApplicationAbstractions.IFlowSlot<ShadowAnalyzeInput, ShadowAnalyzeOutput>
{
}

/// <summary>
/// Defines the closure-planning slot contract for the shadow-extraction flow.
/// </summary>
internal interface ITerrariaRuntimeShadowBuildClosureSlot
    : ApplicationAbstractions.IFlowSlot<ShadowBuildClosureInput, ShadowBuildClosureOutput>
{
}

/// <summary>
/// Defines the workspace-write slot contract for the shadow-extraction flow.
/// </summary>
internal interface ITerrariaRuntimeShadowWriteWorkspaceSlot
    : ApplicationAbstractions.IFlowSlot<ShadowWriteWorkspaceInput, ShadowWriteWorkspaceOutput>
{
}

/// <summary>
/// Defines the workspace-build slot contract for the shadow-extraction flow.
/// </summary>
internal interface ITerrariaRuntimeShadowBuildSlot
    : ApplicationAbstractions.IFlowSlot<ShadowBuildInput, ShadowBuildOutput>
{
}

/// <summary>
/// Defines the terminal persist slot contract for the shadow-extraction flow.
/// </summary>
internal interface ITerrariaRuntimeShadowPersistSlot
    : ApplicationAbstractions.IFlowSlot<ShadowPersistInput, ModelExecution.RunResult>
{
}

/// <summary>
/// Creates the default shadow-extraction slot adapters from the current service graph.
/// </summary>
internal static class TerrariaRuntimeShadowExtractionSlotAdapters
{
    public static TerrariaRuntimeShadowExtractionFlowSlots CreateDefaults(
        ShadowExtractionPipelineDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        return new TerrariaRuntimeShadowExtractionFlowSlots(
            new TerrariaRuntimeShadowResolveInputSlotAdapter(
                dependencies.InputResolver,
                dependencies.ProgressReporter),
            new TerrariaRuntimeShadowAnalyzeSlotAdapter(
                dependencies.AnalysisStage,
                dependencies.ProgressReporter),
            new TerrariaRuntimeShadowBuildClosureSlotAdapter(
                dependencies.ClosurePlanner,
                dependencies.ProgressReporter),
            new TerrariaRuntimeShadowWriteWorkspaceSlotAdapter(
                dependencies.WorkspaceWriter,
                dependencies.ProgressReporter),
            new TerrariaRuntimeShadowBuildSlotAdapter(
                dependencies.BuildExecutor,
                dependencies.ReportBuilder,
                dependencies.ProgressReporter),
            new TerrariaRuntimeShadowPersistSlotAdapter(
                dependencies.ReportStore,
                dependencies.ProgressReporter));
    }
}

internal sealed class TerrariaRuntimeShadowResolveInputSlotAdapter(
    IShadowExtractionInputResolver inputResolver,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeShadowResolveInputSlot
{
    public async Task<ShadowResolveInputOutput> ExecuteAsync(
        ShadowResolveInputInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var resolution = await inputResolver.ResolveAsync(input.Request, progressReporter, cancellationToken);
        return !resolution.IsSuccess || resolution.Value == null
            ? new ShadowResolveInputOutput(null, resolution.FailureCode, resolution.Message ?? "Shadow input resolution failed.")
            : new ShadowResolveInputOutput(resolution.Value);
    }
}

internal sealed class TerrariaRuntimeShadowAnalyzeSlotAdapter(
    IShadowExtractionAnalysisStage analysisStage,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeShadowAnalyzeSlot
{
    public async Task<ShadowAnalyzeOutput> ExecuteAsync(
        ShadowAnalyzeInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var inputResolution = input.ResolveInput.InputResolution
            ?? throw new InvalidOperationException("Input resolution is required before shadow analysis.");
        var analysis = await analysisStage.AnalyzeAsync(inputResolution, progressReporter, cancellationToken);
        return !analysis.IsSuccess || analysis.Value == null
            ? new ShadowAnalyzeOutput(null, analysis.FailureCode, analysis.Message ?? "Shadow analysis failed.")
            : new ShadowAnalyzeOutput(analysis.Value);
    }
}

internal sealed class TerrariaRuntimeShadowBuildClosureSlotAdapter(
    IShadowClosurePlanner closurePlanner,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeShadowBuildClosureSlot
{
    public Task<ShadowBuildClosureOutput> ExecuteAsync(
        ShadowBuildClosureInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var analysis = input.Analyze.Analysis
            ?? throw new InvalidOperationException("Analysis is required before closure planning.");
        var closurePlan = closurePlanner.BuildPlan(analysis, progressReporter, cancellationToken);
        return Task.FromResult(
            !closurePlan.IsSuccess || closurePlan.Value == null
                ? new ShadowBuildClosureOutput(
                    analysis,
                    null,
                    closurePlan.FailureCode,
                    closurePlan.Message ?? "Shadow closure planning failed.")
                : new ShadowBuildClosureOutput(analysis, closurePlan.Value));
    }
}

internal sealed class TerrariaRuntimeShadowWriteWorkspaceSlotAdapter(
    IShadowWorkspaceWriter workspaceWriter,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeShadowWriteWorkspaceSlot
{
    public async Task<ShadowWriteWorkspaceOutput> ExecuteAsync(
        ShadowWriteWorkspaceInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var analysis = input.BuildClosure.Analysis
            ?? throw new InvalidOperationException("Analysis is required before workspace write.");
        var closurePlan = input.BuildClosure.ClosurePlan
            ?? throw new InvalidOperationException("Closure plan is required before workspace write.");
        var inputResolution = analysis.Input;
        var workspaceWrite = await workspaceWriter.WriteAsync(
            inputResolution,
            analysis,
            closurePlan,
            progressReporter,
            cancellationToken);
        return !workspaceWrite.IsSuccess || workspaceWrite.Value == null
            ? new ShadowWriteWorkspaceOutput(
                input.BuildClosure,
                null,
                workspaceWrite.FailureCode,
                workspaceWrite.Message ?? "Shadow workspace write failed.")
            : ReportWriteSummary(input.BuildClosure, workspaceWrite.Value, progressReporter);
    }

    private static ShadowWriteWorkspaceOutput ReportWriteSummary(
        ShadowBuildClosureOutput buildClosure,
        ShadowWorkspaceWriteResult workspaceWriteResult,
        ITerrariaRuntimeProgressReporter progressReporter)
    {
        progressReporter.Report(
            $"[tr-shadow] Rewrite summary: preserved={workspaceWriteResult.RewriteSummary.PreservedMembers}, defaulted={workspaceWriteResult.RewriteSummary.DefaultedMembers}, emptied={workspaceWriteResult.RewriteSummary.EmptiedMembers}");
        return new ShadowWriteWorkspaceOutput(buildClosure, workspaceWriteResult);
    }
}

internal sealed class TerrariaRuntimeShadowBuildSlotAdapter(
    ITerrariaRuntimeBuildExecutor buildExecutor,
    IShadowExtractionReportBuilder reportBuilder,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeShadowBuildSlot
{
    public async Task<ShadowBuildOutput> ExecuteAsync(
        ShadowBuildInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var analysis = input.WriteWorkspace.BuildClosure.Analysis
            ?? throw new InvalidOperationException("Analysis is required before build.");
        var closurePlan = input.WriteWorkspace.BuildClosure.ClosurePlan
            ?? throw new InvalidOperationException("Closure plan is required before build.");
        var workspaceWriteResult = input.WriteWorkspace.WorkspaceWriteResult
            ?? throw new InvalidOperationException("Workspace write result is required before build.");
        var report = reportBuilder.Build(
            analysis.Input,
            analysis,
            closurePlan,
            workspaceWriteResult);
        progressReporter.Report("[tr-shadow] Building shadow workspace...");
        var buildSummary = await buildExecutor.ExecuteAsync(
            TerrariaRuntimeShadowStageHelpers.ToRuntimeLayout(analysis.Input.Layout),
            progressReporter,
            cancellationToken);
        var updatedReport = report with { TrBuildSummary = buildSummary };
        return new ShadowBuildOutput(input.WriteWorkspace, updatedReport, buildSummary);
    }
}

internal sealed class TerrariaRuntimeShadowPersistSlotAdapter(
    IShadowExtractionReportStore reportStore,
    ITerrariaRuntimeProgressReporter progressReporter) : ITerrariaRuntimeShadowPersistSlot
{
    public async Task<ModelExecution.RunResult> ExecuteAsync(
        ShadowPersistInput input,
        ApplicationAbstractions.IFlowExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var layout = input.Build.WriteWorkspace.BuildClosure.Analysis?.Input.Layout
            ?? throw new InvalidOperationException("Input resolution is required before persistence.");
        var reportPath = Path.Combine(layout.ArtifactsPath, "shadow-report.json");
        progressReporter.Report("[tr-shadow] Persisting shadow report...");
        await reportStore.SaveAsync(reportPath, input.Build.Report, cancellationToken);

        if (!input.Build.BuildSummary.BuildSucceeded)
        {
            progressReporter.Report(
                $"[tr-shadow] Build failed with {TerrariaRuntimeShadowStageHelpers.CountBuildErrors(input.Build.BuildSummary.StandardOutput, input.Build.BuildSummary.StandardError)} reported errors.");
            return ModelExecution.RunResult.Failure(
                ApplicationAbstractions.FailureCode.BuildFailed,
                layout.OutputRootPath,
                input.Build.BuildSummary.StandardError);
        }

        progressReporter.Report("[tr-shadow] Shadow extraction pipeline completed.");
        return ModelExecution.RunResult.Success(layout.OutputRootPath, reportPath);
    }
}
