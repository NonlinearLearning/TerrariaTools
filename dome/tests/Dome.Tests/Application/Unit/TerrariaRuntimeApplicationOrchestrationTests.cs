using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeApplicationOrchestrationTests
{
    [Fact]
    public async Task RunAsync_Success_InvokesStagesInOrderAndPersistsUpdatedReport()
    {
        var reportStore = new FakeRunReportStore();
        var workspacePreparer = new FakeTerrariaRuntimeWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationRunner(RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                nameof(ITerrariaRuntimeWorkspacePreparer.EnsureOutputDirectoriesAsync),
                nameof(ITerrariaRuntimeWorkspacePreparer.RefreshDependencyEnvironmentAsync),
                nameof(ITerrariaRuntimeWorkspacePreparer.PrepareWorkspaceAsync)
            ],
            workspacePreparer.Calls);
        Assert.Single(domeRunner.Calls);
        Assert.Single(buildExecutor.Calls);
        Assert.Single(reportStore.LoadedPaths);
        Assert.Single(reportStore.SavedReports);
    }

    [Fact]
    public async Task RunAsync_DomeFailure_SkipsReportLoadAndBuild()
    {
        var reportStore = new FakeRunReportStore();
        var workspacePreparer = new FakeTerrariaRuntimeWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationRunner(RunResult.Failure(FailureCode.AnalysisFailed, "out", "failed"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(reportStore.LoadedPaths);
        Assert.Empty(reportStore.SavedReports);
        Assert.Empty(buildExecutor.Calls);
        Assert.DoesNotContain(nameof(ITerrariaRuntimeWorkspacePreparer.PrepareWorkspaceAsync), workspacePreparer.Calls);
    }

    [Fact]
    public async Task RunAsync_ReportLoadFailure_ReturnsFailureWithoutPreparingWorkspace()
    {
        var reportStore = new FakeRunReportStore(StageResult<RunReport>.Failure(FailureCode.AnalysisFailed, "report load failed"));
        var workspacePreparer = new FakeTerrariaRuntimeWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationRunner(RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.AnalysisFailed, result.FailureCode);
        Assert.Single(reportStore.LoadedPaths);
        Assert.Empty(buildExecutor.Calls);
        Assert.DoesNotContain(nameof(ITerrariaRuntimeWorkspacePreparer.PrepareWorkspaceAsync), workspacePreparer.Calls);
    }

    [Fact]
    public async Task RunAsync_BuildFailure_ReturnsFailureAfterSavingUpdatedReport()
    {
        var reportStore = new FakeRunReportStore();
        var workspacePreparer = new FakeTerrariaRuntimeWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: false, exitCode: 1, standardError: "build failed");
        var domeRunner = new FakeDomeApplicationRunner(RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.BuildFailed, result.FailureCode);
        Assert.Single(reportStore.SavedReports);
    }

    [Fact]
    public async Task RunAsync_ForwardsSolutionPathToDomeRunner()
    {
        var reportStore = new FakeRunReportStore();
        var workspacePreparer = new FakeTerrariaRuntimeWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationRunner(RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(domeRunner.Calls);
        Assert.Equal("input.sln", request.InputPath);
    }

    private sealed class FakeTerrariaRuntimeLayoutFactory : ITerrariaRuntimeLayoutFactory
    {
        public TerrariaRuntimeLayout Create(TerrariaRuntimeRunRequest request) =>
            new(
                request.SolutionPath,
                "source",
                request.OutputRootPath,
                "dependency",
                "workspace",
                "artifacts",
                "workspace\\TerrariaServer.sln");
    }
}
