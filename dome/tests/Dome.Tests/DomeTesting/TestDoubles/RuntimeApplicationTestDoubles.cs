using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Reporting;

namespace TerrariaTools.Dome.Tests.Testing.TestDoubles;

internal sealed class FakeDomeApplicationRunner(RunResult result) : IDomeApplicationRunner
{
    public List<RunRequest> Calls { get; } = [];

    public Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(result);
    }
}

internal sealed class FakeRunReportStore : IRunReportStore
{
    private readonly StageResult<RunReport> _loadResult;

    public FakeRunReportStore(StageResult<RunReport>? loadResult = null)
    {
        _loadResult = loadResult ?? StageResult<RunReport>.Success(new RunReport(
            true,
            FailureCode.None,
            0,
            0,
            0,
            0,
            [],
            null,
            [],
            new RiskSummary(0, []),
            new PlanCoverageSummary(0, 0, []),
            null,
            null,
            null,
            WorkspaceLoadMode.SourceOnly,
            false,
            [],
            null));
    }

    public List<string> LoadedPaths { get; } = [];
    public List<(string Path, RunReport Report)> SavedReports { get; } = [];

    public Task<StageResult<RunReport>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadedPaths.Add(path);
        return Task.FromResult(_loadResult);
    }

    public Task SaveAsync(string path, RunReport report, CancellationToken cancellationToken)
    {
        SavedReports.Add((path, report));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTerrariaRuntimeWorkspacePreparer : ITerrariaRuntimeWorkspacePreparer
{
    public List<string> Calls { get; } = [];

    public Task EnsureOutputDirectoriesAsync(TerrariaRuntimeLayout layout, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(EnsureOutputDirectoriesAsync));
        return Task.CompletedTask;
    }

    public Task RefreshDependencyEnvironmentAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(RefreshDependencyEnvironmentAsync));
        return Task.CompletedTask;
    }

    public Task PrepareWorkspaceAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(PrepareWorkspaceAsync));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTerrariaRuntimeBuildExecutor(bool success, int exitCode, string? standardError = null) : ITerrariaRuntimeBuildExecutor
{
    public List<TerrariaRuntimeLayout> Calls { get; } = [];

    public Task<TerrariaRuntimeBuildSummary> ExecuteAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        Calls.Add(layout);
        return Task.FromResult(new TerrariaRuntimeBuildSummary(
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

internal sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
{
    public List<string> Messages { get; } = [];

    public void Report(string message)
    {
        Messages.Add(message);
    }
}

internal sealed class FakeShadowExtractionInputResolver(StageResult<ShadowExtractionInputResolution> result) : IShadowExtractionInputResolver
{
    public List<TerrariaRuntimeShadowExtractionRequest> Calls { get; } = [];

    public Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(result);
    }
}

internal sealed class FakeShadowExtractionAnalysisStage(StageResult<ShadowExtractionAnalysis> result) : IShadowExtractionAnalysisStage
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

internal sealed class FakeShadowClosurePlanner(StageResult<ShadowClosurePlan> result) : IShadowClosurePlanner
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

internal sealed class FakeShadowWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult> result) : IShadowWorkspaceWriter
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

internal sealed class FakeShadowExtractionReportBuilder(TerrariaRuntimeShadowExtractionReport report) : IShadowExtractionReportBuilder
{
    public List<(ShadowExtractionInputResolution Input, ShadowExtractionAnalysis Analysis, ShadowClosurePlan Closure, ShadowWorkspaceWriteResult Write)> Calls { get; } = [];

    public TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        Calls.Add((input, analysis, closurePlan, workspaceWriteResult));
        return report;
    }
}

internal sealed class FakeShadowExtractionReportStore : IShadowExtractionReportStore
{
    public List<(string Path, TerrariaRuntimeShadowExtractionReport Report)> Saves { get; } = [];

    public Task SaveAsync(string path, TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken)
    {
        Saves.Add((path, report));
        return Task.CompletedTask;
    }
}
