using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using PortsCommon = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeShadowExtractionApplicationOrchestrationTests
{
    [Fact]
    public async Task RunAsync_InputResolveFailure_StopsPipeline()
    {
        var app = TerrariaRuntimeShadowExtractionCompositionRoot.Create(
            new ShadowExtractionPipelineDependencies(
                new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Failure(PortsCommon.FailureCode.WorkspaceLoadFailed, "load failed")),
                new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Failure(PortsCommon.FailureCode.AnalysisFailed, "unused")),
                new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Failure(PortsCommon.FailureCode.AnalysisFailed, "unused")),
                new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(PortsCommon.FailureCode.RewriteFailed, "unused")),
                new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0),
                new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
                new FakeShadowExtractionCompatibilityReportStore(),
                new FakeTerrariaRuntimeCompatibilityProgressReporter()));

        var result = await app.RunAsync(new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.WorkspaceLoadFailed, result.FailureCode);
    }

    [Fact]
    public async Task RunAsync_AnalysisFailure_StopsBeforeClosurePlanning()
    {
        var input = CreateInput();
        var analysisStage = new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Failure(PortsCommon.FailureCode.AnalysisFailed, "analysis failed"));
        var closurePlanner = new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Failure(PortsCommon.FailureCode.AnalysisFailed, "unused"));
        var app = TerrariaRuntimeShadowExtractionCompositionRoot.Create(
            new ShadowExtractionPipelineDependencies(
                new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
                analysisStage,
                closurePlanner,
                new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(PortsCommon.FailureCode.RewriteFailed, "unused")),
                new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0),
                new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
                new FakeShadowExtractionCompatibilityReportStore(),
                new FakeTerrariaRuntimeCompatibilityProgressReporter()));

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.AnalysisFailed, result.FailureCode);
        Assert.Single(analysisStage.Calls);
        Assert.Empty(closurePlanner.Calls);
    }

    [Fact]
    public async Task RunAsync_WorkspaceWriteFailure_StopsBeforeBuildAndReportWrite()
    {
        var input = CreateInput();
        var analysis = CreateAnalysis(input);
        var closure = CreateClosure();
        var workspaceWriter = new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Failure(PortsCommon.FailureCode.RewriteFailed, "rewrite failed"));
        var reportStore = new FakeShadowExtractionCompatibilityReportStore();
        var buildExecutor = new FakeTerrariaRuntimeCompatibilityBuildExecutor(success: true, exitCode: 0);
        var app = TerrariaRuntimeShadowExtractionCompositionRoot.Create(
            new ShadowExtractionPipelineDependencies(
                new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
                new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Success(analysis)),
                new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Success(closure)),
                workspaceWriter,
                buildExecutor,
                new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
                reportStore,
                new FakeTerrariaRuntimeCompatibilityProgressReporter()));

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.RewriteFailed, result.FailureCode);
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
        var app = TerrariaRuntimeShadowExtractionCompositionRoot.Create(
            new ShadowExtractionPipelineDependencies(
                new FakeShadowExtractionCompatibilityInputResolver(StageResult<ShadowExtractionInputResolution>.Success(input)),
                new FakeShadowExtractionCompatibilityAnalysisStage(StageResult<ShadowExtractionAnalysis>.Success(analysis)),
                new FakeShadowCompatibilityClosurePlanner(StageResult<ShadowClosurePlan>.Success(closure)),
                new FakeShadowCompatibilityWorkspaceWriter(StageResult<ShadowWorkspaceWriteResult>.Success(writeResult)),
                buildExecutor,
                new FakeShadowExtractionCompatibilityReportBuilder(CreateReport()),
                reportStore,
                new FakeTerrariaRuntimeCompatibilityProgressReporter()));

        var result = await app.RunAsync(input.Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.BuildFailed, result.FailureCode);
        Assert.Single(buildExecutor.Calls);
        Assert.Single(reportStore.Saves);
    }

    private static ShadowExtractionInputResolution CreateInput()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed");
        var layout = new ApplicationAbstractions.TerrariaRuntimeShadowLayout("input.sln", "source", "out", "workspace", "artifacts", "dependency", "workspace\\input.sln");
        var source = new ModelAnalysis.SourceDocument("Main.cs", "Main.cs", "class C {}");
        var loadResult = ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ModelAnalysis.AnalysisInput(new ModelAnalysis.SourceDocumentSet("Main.cs", string.Empty, [source]), ModelAnalysis.AnalysisInputMode.SourceOnly),
            PortsCommon.WorkspaceLoadMode.SourceOnly,
            "Stub");
        return new ShadowExtractionInputResolution(request, layout, loadResult);
    }

    private static ShadowExtractionAnalysis CreateAnalysis(ShadowExtractionInputResolution input)
    {
        var source = new ModelAnalysis.SourceDocument("Main.cs", "Main.cs", "class C {}");
        var engineResult = new ApplicationAnalysisCompatibilityResultBuilder().BuildEngineResult(source);
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
        return new ShadowExtractionAnalysis(input, engineResult);

    }

    private static ShadowClosurePlan CreateClosure() =>
        new(
            new ModelAnalysis.FunctionNodeRef(
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
                "void"),
            ["Main.cs"],
            [new ModelPrimitives.MemberId("Sample.Main.Run()")],
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase),
            1);

    private static ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport CreateReport() =>
        new("Seed", "Sample.Main.Run()", ["Main.cs"], ["Sample.Main.Run()"], new ModelAnalysis.AdvancedAnalysisSummary(), 1, new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, [], [], []));
}



