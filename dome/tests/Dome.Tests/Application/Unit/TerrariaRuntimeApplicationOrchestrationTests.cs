using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

/// <summary>
/// Runtime legacy exception-path orchestration tests. These assertions are not the standard DomeApplication baseline.
/// </summary>
public sealed class TerrariaRuntimeApplicationOrchestrationLegacyTests
{
    [Fact]
    public async Task RunAsync_Success_InvokesStagesInOrderAndPersistsUpdatedReport()
    {
        var reportStore = new FakeRunReportCompatibilityStore();
        var workspacePreparer = new FakeTerrariaRuntimeCompatibilityWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationCompatibilityRunner(ApplicationAbstractions.RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

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
        var reportStore = new FakeRunReportCompatibilityStore();
        var workspacePreparer = new FakeTerrariaRuntimeCompatibilityWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationCompatibilityRunner(ApplicationAbstractions.RunResult.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "out", "failed"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(reportStore.LoadedPaths);
        Assert.Empty(reportStore.SavedReports);
        Assert.Empty(buildExecutor.Calls);
        Assert.DoesNotContain(nameof(ITerrariaRuntimeWorkspacePreparer.PrepareWorkspaceAsync), workspacePreparer.Calls);
    }

    [Fact]
    public async Task RunAsync_ReportLoadFailure_ReturnsFailureWithoutPreparingWorkspace()
    {
        var reportStore = new FakeRunReportCompatibilityStore(StageResult<ApplicationAbstractions.RunReport>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "report load failed"));
        var workspacePreparer = new FakeTerrariaRuntimeCompatibilityWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationCompatibilityRunner(ApplicationAbstractions.RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.AnalysisFailed, result.FailureCode);
        Assert.Single(reportStore.LoadedPaths);
        Assert.Empty(buildExecutor.Calls);
        Assert.DoesNotContain(nameof(ITerrariaRuntimeWorkspacePreparer.PrepareWorkspaceAsync), workspacePreparer.Calls);
    }

    [Fact]
    public async Task RunAsync_BuildFailure_ReturnsFailureAfterSavingUpdatedReport()
    {
        var reportStore = new FakeRunReportCompatibilityStore();
        var workspacePreparer = new FakeTerrariaRuntimeCompatibilityWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: false, exitCode: 1, standardError: "build failed");
        var domeRunner = new FakeDomeApplicationCompatibilityRunner(ApplicationAbstractions.RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.BuildFailed, result.FailureCode);
        Assert.Single(reportStore.SavedReports);
    }

    [Fact]
    public async Task RunAsync_ForwardsSolutionPathToDomeRunner()
    {
        var reportStore = new FakeRunReportCompatibilityStore();
        var workspacePreparer = new FakeTerrariaRuntimeCompatibilityWorkspacePreparer();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0);
        var domeRunner = new FakeDomeApplicationCompatibilityRunner(ApplicationAbstractions.RunResult.Success("artifacts", "report.json"));
        var app = new TerrariaRuntimeApplication(
            domeRunner,
            workspacePreparer,
            buildExecutor,
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter(),
            new FakeTerrariaRuntimeLayoutFactory());

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(domeRunner.Calls);
        Assert.Equal("input.sln", request.InputPath);
    }

    private sealed class FakeTerrariaRuntimeLayoutFactory : ITerrariaRuntimeLayoutFactory
    {
        public ApplicationAbstractions.TerrariaRuntimeLayout Create(ApplicationAbstractions.TerrariaRuntimeRunRequest request) =>
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
