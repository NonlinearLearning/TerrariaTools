using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeShadowExtractionApplicationOrchestrationTests
{
    [Fact]
    public async Task RunAsync_InputResolveFailure_StopsPipeline()
    {
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionInputResolver(StageResult<ShadowExtractionInputResolution>.Failure(FailureCode.WorkspaceLoadFailed, "load failed")),
            new FakeShadowExtractionAnalysisStage(StageResult<ShadowExtractionAnalysis>.Failure(FailureCode.AnalysisFailed, "unused")),
            new FakeShadowClosurePlanner(StageResult<ShadowClosurePlan>.Failure(FailureCode.AnalysisFailed, "unused")),
            new FakeShadowWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(FailureCode.RewriteFailed, "unused")),
            new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0),
            new FakeShadowExtractionReportBuilder(CreateReport()),
            new FakeShadowExtractionReportStore(),
            new FakeTerrariaRuntimeProgressReporter());

        var result = await app.RunAsync(new TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.WorkspaceLoadFailed, result.FailureCode);
    }

    [Fact]
    public async Task RunAsync_AnalysisFailure_StopsBeforeClosurePlanning()
    {
        var input = CreateInput();
        var analysisStage = new FakeShadowExtractionAnalysisStage(StageResult<ShadowExtractionAnalysis>.Failure(FailureCode.AnalysisFailed, "analysis failed"));
        var closurePlanner = new FakeShadowClosurePlanner(StageResult<ShadowClosurePlan>.Failure(FailureCode.AnalysisFailed, "unused"));
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
            analysisStage,
            closurePlanner,
            new FakeShadowWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(FailureCode.RewriteFailed, "unused")),
            new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0),
            new FakeShadowExtractionReportBuilder(CreateReport()),
            new FakeShadowExtractionReportStore(),
            new FakeTerrariaRuntimeProgressReporter());

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.AnalysisFailed, result.FailureCode);
        Assert.Single(analysisStage.Calls);
        Assert.Empty(closurePlanner.Calls);
    }

    [Fact]
    public async Task RunAsync_WorkspaceWriteFailure_StopsBeforeBuildAndReportWrite()
    {
        var input = CreateInput();
        var analysis = CreateAnalysis(input);
        var closure = CreateClosure();
        var workspaceWriter = new FakeShadowWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(FailureCode.RewriteFailed, "rewrite failed"));
        var reportStore = new FakeShadowExtractionReportStore();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0);
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
            new FakeShadowExtractionAnalysisStage(StageResult<ShadowExtractionAnalysis>.Success(analysis)),
            new FakeShadowClosurePlanner(StageResult<ShadowClosurePlan>.Success(closure)),
            workspaceWriter,
            buildExecutor,
            new FakeShadowExtractionReportBuilder(CreateReport()),
            reportStore,
            new FakeTerrariaRuntimeProgressReporter());

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
        Assert.Single(workspaceWriter.Calls);
        Assert.Empty(buildExecutor.Calls);
        Assert.Empty(reportStore.Saves);
    }

    [Fact]
    public async Task RunAsync_BuildFailure_PersistsReportAndReturnsFailure()
    {
        var input = CreateInput();
        var analysis = CreateAnalysis(input);
        var closure = CreateClosure();
        var writeResult = new ShadowWorkspaceWriteResult(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Main.cs"] = "code" },
            new TerrariaRuntimeShadowRewriteSummary(1, 0, 0, ["A"], [], []));
        var reportStore = new FakeShadowExtractionReportStore();
        var buildExecutor = new FakeTerrariaRuntimeBuildExecutor(success: false, exitCode: 1, standardError: "build failed");
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
            new FakeShadowExtractionAnalysisStage(StageResult<ShadowExtractionAnalysis>.Success(analysis)),
            new FakeShadowClosurePlanner(StageResult<ShadowClosurePlan>.Success(closure)),
            new FakeShadowWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Success(writeResult)),
            buildExecutor,
            new FakeShadowExtractionReportBuilder(CreateReport()),
            reportStore,
            new FakeTerrariaRuntimeProgressReporter());

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.BuildFailed, result.FailureCode);
        Assert.Single(buildExecutor.Calls);
        Assert.Single(reportStore.Saves);
    }

    private static ShadowExtractionInputResolution CreateInput()
    {
        var request = new TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed");
        var layout = new TerrariaRuntimeShadowLayout("input.sln", "source", "out", "workspace", "artifacts", "dependency", "workspace\\input.sln");
        var source = new SourceDocument("Main.cs", "Main.cs", "class C {}");
        var loadResult = WorkspaceLoadResult.Success([source], WorkspaceLoadMode.SourceOnly, "Stub");
        return new ShadowExtractionInputResolution(request, layout, loadResult);
    }

    private static ShadowExtractionAnalysis CreateAnalysis(ShadowExtractionInputResolution input)
    {
        var source = new SourceDocument("Main.cs", "Main.cs", "class C {}");
        var engineResult = new TestAnalysisContextBuilder().BuildEngineResult(source);
        var context = engineResult.CreateContext();
        var seedNode = new FunctionNodeRef(
            new MemberId("Sample.Main.Run()"),
            MemberKind.Method,
            "Sample.Main",
            "Run",
            "Main.cs",
            0,
            0,
            false,
            true,
            true,
            true,
            "void");
        return new ShadowExtractionAnalysis(input, engineResult, context, seedNode);
    }

    private static ShadowClosurePlan CreateClosure() =>
        new(["Main.cs"], [new MemberId("Sample.Main.Run()")], new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase), 1);

    private static TerrariaRuntimeShadowExtractionReport CreateReport() =>
        new("Seed", "Sample.Main.Run()", ["Main.cs"], ["Sample.Main.Run()"], new AdvancedAnalysisSummary(0, 0, [], [], 0, 0, [], [], [], [], 0, 0, 0, 0), 1, new TerrariaRuntimeShadowRewriteSummary(1, 0, 0, [], [], []));
}
