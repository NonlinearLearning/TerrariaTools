using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

/// <summary>
/// Shadow-extraction legacy exception-path orchestration tests. These assertions are not the standard DomeApplication baseline.
/// </summary>
public sealed class TerrariaRuntimeShadowExtractionApplicationOrchestrationLegacyTests
{
    [Fact]
    public async Task RunAsync_InputResolveFailure_StopsPipeline()
    {
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Failure(ModelPrimitives.FailureCode.WorkspaceLoadFailed, "load failed")),
            new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "unused")),
            new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "unused")),
            new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(ModelPrimitives.FailureCode.RewriteFailed, "unused")),
            new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0),
            new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
            new FakeShadowExtractionCompatibilityReportStore(),
            new FakeTerrariaRuntimeCompatibilityProgressReporter());

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.WorkspaceLoadFailed, result.FailureCode);
    }

    [Fact]
    public async Task RunAsync_AnalysisFailure_StopsBeforeClosurePlanning()
    {
        var input = CreateInput();
        var analysisStage = new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "analysis failed"));
        var closurePlanner = new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "unused"));
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
            analysisStage,
            closurePlanner,
            new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(ModelPrimitives.FailureCode.RewriteFailed, "unused")),
            new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0),
            new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
            new FakeShadowExtractionCompatibilityReportStore(),
            new FakeTerrariaRuntimeCompatibilityProgressReporter());

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.AnalysisFailed, result.FailureCode);
        Assert.Single(analysisStage.Calls);
        Assert.Empty(closurePlanner.Calls);
    }

    [Fact]
    public async Task RunAsync_WorkspaceWriteFailure_StopsBeforeBuildAndReportWrite()
    {
        var input = CreateInput();
        var analysis = CreateAnalysis(input);
        var closure = CreateClosure();
        var workspaceWriter = new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(ModelPrimitives.FailureCode.RewriteFailed, "rewrite failed"));
        var reportStore = new FakeShadowExtractionCompatibilityReportStore();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0);
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
            new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Success(analysis)),
            new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Success(closure)),
            workspaceWriter,
            buildExecutor,
            new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter());

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.RewriteFailed, result.FailureCode);
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
            new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, ["A"], [], []));
        var reportStore = new FakeShadowExtractionCompatibilityReportStore();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: false, exitCode: 1, standardError: "build failed");
        var app = new TerrariaRuntimeShadowExtractionApplication(
            new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
            new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Success(analysis)),
            new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Success(closure)),
            new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Success(writeResult)),
            buildExecutor,
            new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
            reportStore,
            new FakeTerrariaRuntimeCompatibilityProgressReporter());

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.BuildFailed, result.FailureCode);
        Assert.Single(buildExecutor.Calls);
        Assert.Single(reportStore.Saves);
    }

    private static ShadowExtractionInputResolution CreateInput()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed");
        var layout = new ApplicationAbstractions.TerrariaRuntimeShadowLayout("input.sln", "source", "out", "workspace", "artifacts", "dependency", "workspace\\input.sln");
        var source = new ApplicationAbstractions.SourceDocument("Main.cs", "Main.cs", "class C {}");
        var loadResult = ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ApplicationAbstractions.SourceDocumentSet("Main.cs", string.Empty, [source]),
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            "Stub");
        return new ShadowExtractionInputResolution(request, layout, loadResult);
    }

    private static ShadowExtractionAnalysis CreateAnalysis(ShadowExtractionInputResolution input)
    {
        var source = new ApplicationAbstractions.SourceDocument("Main.cs", "Main.cs", "class C {}");
        var engineResult = new ApplicationAnalysisCompatibilityResultBuilder().BuildEngineResult(source);
        var context = engineResult.CreateContext();
        var seedNode = new ModelAnalysis.FunctionNodeRef(
            new ModelPrimitives.MemberId("Sample.Main.Run()"),
            ModelPrimitives.MemberKind.Method,
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
        return new ShadowExtractionAnalysis(input, engineResult, context, seedNode, []);
    }

    private static ShadowClosurePlan CreateClosure() =>
        new(["Main.cs"], [new ModelPrimitives.MemberId("Sample.Main.Run()")], new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase), 1);

    private static ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport CreateReport() =>
        new("Seed", "Sample.Main.Run()", ["Main.cs"], ["Sample.Main.Run()"], new ModelAnalysis.AdvancedAnalysisSummary(), 1, new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, [], [], []));
}
