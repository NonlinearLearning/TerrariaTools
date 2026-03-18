using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Reporting;

namespace TerrariaTools.Dome.Tests.Testing.TestDoubles;

/// <summary>
/// Compatibility-only doubles for runtime/shadow legacy execution paths on native contracts.
/// </summary>
internal sealed class FakeDomeApplicationCompatibilityRunner(ApplicationAbstractions.RunResult result) : IDomeApplicationRunner
{
    public List<ApplicationAbstractions.RunRequest> Calls { get; } = [];

    public Task<ApplicationAbstractions.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(result);
    }
}

internal sealed class FakeRunReportCompatibilityStore : IRunReportStore
{
    private readonly StageResult<ApplicationAbstractions.RunReport> _loadResult;

    public FakeRunReportCompatibilityStore(StageResult<ApplicationAbstractions.RunReport>? loadResult = null)
    {
        _loadResult = loadResult ?? StageResult<ApplicationAbstractions.RunReport>.Success(new ApplicationAbstractions.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
            0,
            0,
            0,
            0,
            [],
            null,
            [],
            new ApplicationAbstractions.RiskSummary(0, []),
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, []),
            null,
            null,
            null,
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            false,
            [],
            null));
    }

    public List<string> LoadedPaths { get; } = [];
    public List<(string Path, ApplicationAbstractions.RunReport Report)> SavedReports { get; } = [];

    public Task<StageResult<ApplicationAbstractions.RunReport>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadedPaths.Add(path);
        return Task.FromResult(_loadResult);
    }

    public Task SaveAsync(string path, ApplicationAbstractions.RunReport report, CancellationToken cancellationToken)
    {
        SavedReports.Add((path, report));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTerrariaRuntimeCompatibilityWorkspacePreparer : ITerrariaRuntimeWorkspacePreparer
{
    public List<string> Calls { get; } = [];

    public Task EnsureOutputDirectoriesAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(EnsureOutputDirectoriesAsync));
        return Task.CompletedTask;
    }

    public Task RefreshDependencyEnvironmentAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(RefreshDependencyEnvironmentAsync));
        return Task.CompletedTask;
    }

    public Task PrepareWorkspaceAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(PrepareWorkspaceAsync));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTerrariaRuntimeCompatibilityBuildExecutor(bool success, int exitCode, string? standardError = null) : ITerrariaRuntimeBuildExecutor
{
    public List<ApplicationAbstractions.TerrariaRuntimeLayout> Calls { get; } = [];

    public Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        Calls.Add(layout);
        return Task.FromResult(new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
            success,
            exitCode,
            $"dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m",
            layout.WorkspacePath,
            layout.DependencyEnvironmentPath,
            layout.WorkspaceSolutionPath,
            string.Empty,
            standardError ?? string.Empty));
    }
}

internal sealed class FakeTerrariaRuntimeCompatibilityProgressReporter : ITerrariaRuntimeProgressReporter
{
    public List<string> Messages { get; } = [];

    public void Report(string message)
    {
        Messages.Add(message);
    }
}

internal sealed class FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution> result) : IShadowExtractionInputResolver
{
    public List<ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest> Calls { get; } = [];

    public Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(result);
    }
}

internal sealed class FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis> result) : IShadowExtractionAnalysisStage
{
    public List<ShadowExtractionInputResolution> Calls { get; } = [];

    public Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        Calls.Add(input);
        return Task.FromResult(result);
    }
}

internal sealed class FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan> result) : IShadowClosurePlanner
{
    public List<ShadowExtractionAnalysis> Calls { get; } = [];

    public StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        Calls.Add(analysis);
        return result;
    }
}

internal sealed class FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult> result) : IShadowWorkspaceWriter
{
    public List<(ShadowExtractionInputResolution Input, ShadowExtractionAnalysis Analysis, ShadowClosurePlan Closure)> Calls { get; } = [];

    public Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        Calls.Add((input, analysis, closurePlan));
        return Task.FromResult(result);
    }
}

internal sealed class FakeShadowExtractionCompatibilityReportBuilder(ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report) : IShadowExtractionReportBuilder
{
    public List<(ShadowExtractionInputResolution Input, ShadowExtractionAnalysis Analysis, ShadowClosurePlan Closure, ShadowWorkspaceWriteResult Write)> Calls { get; } = [];

    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        Calls.Add((input, analysis, closurePlan, workspaceWriteResult));
        return report;
    }
}

internal sealed class FakeShadowExtractionCompatibilityReportStore : IShadowExtractionReportStore
{
    public List<(string Path, ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Report)> Saves { get; } = [];

    public Task SaveAsync(string path, ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken)
    {
        Saves.Add((path, report));
        return Task.CompletedTask;
    }
}
