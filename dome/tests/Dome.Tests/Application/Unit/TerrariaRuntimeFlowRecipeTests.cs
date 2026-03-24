using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.Runtime.Host;
using TerrariaTools.Dome.Application.UseCases.Runtime;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeFlowRecipeTests
{
    [Fact]
    public void Standard_UsesPrepareExecuteDomeLoadReportBuildWorkspacePersistOrder()
    {
        var recipe = TerrariaRuntimeFlowRecipes.Standard(CreateSlots());

        Assert.Equal(
            ["prepare", "execute-dome", "load-report", "build-workspace", "persist"],
            recipe.RequiredSlots);
    }

    [Fact]
    public void BuildStages_UsesTerrariaRuntimePipelineContext()
    {
        var recipe = TerrariaRuntimeFlowRecipes.Standard(CreateSlots());

        var stages = TerrariaRuntimeFlowRecipes.BuildStages(recipe);

        Assert.All(stages, stage => Assert.IsAssignableFrom<IPipelineStage<TerrariaRuntimePipelineContext>>(stage));
    }

    [Fact]
    public async Task BuildStages_DelegatesToDomeRunnerAndPersistsReportLast()
    {
        var events = new List<string>();
        var dependencies = new TerrariaRuntimePipelineDependencies(
            new RecordingDomeRunner(events, ModelExecution.RunResult.Success("artifacts", "report.json")),
            new RecordingWorkspacePreparer(events),
            new RecordingBuildExecutor(events, success: true, exitCode: 0),
            new RecordingReportStore(events),
            new FakeTerrariaRuntimeCompatibilityProgressReporter(),
            new TestTerrariaRuntimeLayoutFactory());
        var slots = TerrariaRuntimeSlotAdapters.CreateDefaults(dependencies);
        var recipe = TerrariaRuntimeFlowRecipes.Standard(slots);
        var stages = TerrariaRuntimeFlowRecipes.BuildStages(recipe);
        var runner = new PipelineRunner<TerrariaRuntimePipelineContext>(
            stages,
            new ProgressReporterPipelineObserver<TerrariaRuntimePipelineContext>(_ => { }));
        var context = new TerrariaRuntimePipelineContext(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out"));

        await runner.RunAsync(context, CancellationToken.None);

        Assert.IsType<TerrariaRuntimePipelineContext>(context);
        Assert.NotNull(context.TerminalState);
        Assert.Equal("persist", events[^1]);
        Assert.Single(((RecordingDomeRunner)dependencies.DomeApplication).Calls);
    }

    private static TerrariaRuntimeFlowSlots CreateSlots() =>
        new(
            new StubPrepareSlot(),
            new StubExecuteDomeSlot(),
            new StubLoadReportSlot(),
            new StubBuildWorkspaceSlot(),
            new StubPersistSlot());

    private sealed class StubPrepareSlot : ITerrariaRuntimePrepareSlot
    {
        public Task<TerrariaRuntimePrepareOutput> ExecuteAsync(
            TerrariaRuntimePrepareInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubExecuteDomeSlot : ITerrariaRuntimeExecuteDomeSlot
    {
        public Task<TerrariaRuntimeExecuteDomeOutput> ExecuteAsync(
            TerrariaRuntimeExecuteDomeInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubLoadReportSlot : ITerrariaRuntimeLoadReportSlot
    {
        public Task<StageResult<TerrariaRuntimeLoadReportOutput>> ExecuteAsync(
            TerrariaRuntimeLoadReportInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBuildWorkspaceSlot : ITerrariaRuntimeBuildWorkspaceSlot
    {
        public Task<TerrariaRuntimeBuildWorkspaceOutput> ExecuteAsync(
            TerrariaRuntimeBuildWorkspaceInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubPersistSlot : ITerrariaRuntimePersistSlot
    {
        public Task<ModelExecution.RunResult> ExecuteAsync(
            TerrariaRuntimePersistInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingDomeRunner(List<string> events, ModelExecution.RunResult result) : IDomeApplicationRunner
    {
        public List<ApplicationAbstractions.RunRequest> Calls { get; } = [];

        public Task<ModelExecution.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken)
        {
            events.Add("execute-dome");
            Calls.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingWorkspacePreparer(List<string> events) : ITerrariaRuntimeWorkspacePreparer
    {
        public Task EnsureOutputDirectoriesAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, CancellationToken cancellationToken)
        {
            events.Add("prepare:ensure-directories");
            return Task.CompletedTask;
        }

        public Task RefreshDependencyEnvironmentAsync(
            ApplicationAbstractions.TerrariaRuntimeLayout layout,
            ITerrariaRuntimeProgressReporter progressReporter,
            CancellationToken cancellationToken)
        {
            events.Add("prepare:refresh-environment");
            return Task.CompletedTask;
        }

        public Task PrepareWorkspaceAsync(
            ApplicationAbstractions.TerrariaRuntimeLayout layout,
            ITerrariaRuntimeProgressReporter progressReporter,
            CancellationToken cancellationToken)
        {
            events.Add("build-workspace:prepare-workspace");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBuildExecutor(List<string> events, bool success, int exitCode) : ITerrariaRuntimeBuildExecutor
    {
        public Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(
            ApplicationAbstractions.TerrariaRuntimeLayout layout,
            ITerrariaRuntimeProgressReporter progressReporter,
            CancellationToken cancellationToken)
        {
            events.Add("build-workspace");
            return Task.FromResult(
                new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
                    success,
                    exitCode,
                    "dotnet build",
                    layout.WorkspacePath,
                    layout.DependencyEnvironmentPath,
                    layout.WorkspaceSolutionPath,
                    string.Empty,
                    string.Empty));
        }
    }

    private sealed class RecordingReportStore(List<string> events) : IRunReportStore
    {
        private readonly ModelExecution.RunReport report = new(
            true,
            ApplicationAbstractions.FailureCode.None,
            0,
            0,
            0,
            0,
            [],
            null,
            [],
            new ModelExecution.RiskSummary(0, []),
            new ModelExecution.PlanCoverageSummary(0, 0, []),
            null,
            null,
            null,
            ApplicationAbstractions.WorkspaceLoadMode.SourceOnly,
            false,
            [],
            null);

        public Task<StageResult<ModelExecution.RunReport>> LoadAsync(string path, CancellationToken cancellationToken)
        {
            events.Add("load-report");
            return Task.FromResult(StageResult<ModelExecution.RunReport>.Success(report));
        }

        public Task SaveAsync(string path, ModelExecution.RunReport report, CancellationToken cancellationToken)
        {
            events.Add("persist");
            return Task.CompletedTask;
        }
    }

    private sealed class TestTerrariaRuntimeLayoutFactory : ITerrariaRuntimeLayoutFactory
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
