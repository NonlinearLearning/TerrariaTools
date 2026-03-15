using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using TerrariaTools.Dome.Rules;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using TerrariaTools.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeApplicationOrchestrationTests
{
    [Fact]
    public async Task RunAsync_AnalyzeOnly_EmitsArtifactsWithoutRewriteOutput()
    {
        var source = new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        var analysisResult = new TestAnalysisContextBuilder().BuildEngineResult(source);
        var artifactEmission = new FakeArtifactEmissionService();
        var rewriteOutput = new FakeRewriteOutputStore();
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success([source], WorkspaceLoadMode.SourceOnly, "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            artifactEmissionService: artifactEmission,
            rewriteOutputStore: rewriteOutput);

        var result = await app.RunAsync(new RunRequest("in", "out", Array.Empty<string>(), RunMode.AnalyzeOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(artifactEmission.Calls);
        Assert.True(artifactEmission.Calls[0].ArtifactPlan.WriteAnalysis);
        Assert.Empty(rewriteOutput.Writes);
    }

    [Fact]
    public async Task RunAsync_PlanOnly_MergesInitialAndPredictedDecisionsThroughInjectedAnalyzers()
    {
        var memberId = new MemberId("Sample.Player.Update()");
        var target = new PlanTarget("Sample.cs", memberId, MemberKind.Method, TargetKind.Statement, 0, 10, "int count = 1;");
        var source = new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        var analysisResult = new TestAnalysisContextBuilder()
            .AddTarget(new AnalysisTarget(
                target,
                false,
                [new DirectiveAction(PlanActionKind.Delete, null, "dome:delete", "delete")],
                [],
                [],
                [],
                StatementKindRef.Declaration,
                false,
                false,
                false,
                [],
                StatementScopeMode.MinimalBlock,
                "scope",
                null))
            .BuildEngineResult(source);
        var artifactEmission = new FakeArtifactEmissionService();
        var predictionTarget = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Run()"), MemberKind.Method, TargetKind.Method, 20, 5, "Run");
        var predictionAnalyzer = new FakeReferenceZeroPredictionAnalyzer(
            MarkDecision.ForTarget(
                predictionTarget,
                PlanActionKind.Delete,
                "reference-zero-prediction",
                "predicted",
                origin: DecisionOrigin.Prediction));
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success([source], WorkspaceLoadMode.SourceOnly, "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            predictionAnalyzer: predictionAnalyzer,
            artifactEmissionService: artifactEmission);

        var result = await app.RunAsync(new RunRequest("in", "out", Array.Empty<string>(), RunMode.PlanOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(predictionAnalyzer.ObservedInitialDecisionCounts);
        Assert.Equal(1, predictionAnalyzer.ObservedInitialDecisionCounts[0]);
        Assert.Single(artifactEmission.Calls);
        Assert.NotNull(artifactEmission.Calls[0].Plan);
        Assert.Equal(2, artifactEmission.Calls[0].Plan!.Changes.Count);
    }

    [Fact]
    public async Task RunAsync_Standard_RewriteExecutorFailure_EmitsFailureReportWithoutRewriteOutput()
    {
        var analysisResult = BuildStandardAnalysisResult();
        var artifactEmission = new FakeArtifactEmissionService();
        var rewriteOutput = new FakeRewriteOutputStore();
        var rewriteExecutor = new FakeRewriteExecutor(RewriteExecutionResult.Failure("rewrite broke"));
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success([new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample;")], WorkspaceLoadMode.SourceOnly, "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            rewriteExecutor: rewriteExecutor,
            artifactEmissionService: artifactEmission,
            rewriteOutputStore: rewriteOutput);

        var result = await app.RunAsync(new RunRequest("in", "out", Array.Empty<string>(), RunMode.Standard), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
        Assert.Single(rewriteExecutor.Calls);
        Assert.Single(artifactEmission.Calls);
        Assert.NotNull(artifactEmission.Calls[0].Report.FailureSummary);
        Assert.Empty(rewriteOutput.Writes);
    }

    [Fact]
    public async Task RunAsync_Standard_RewriteOutputStoreFailure_StopsWithRewriteFailure()
    {
        var analysisResult = BuildStandardAnalysisResult();
        var artifactEmission = new FakeArtifactEmissionService();
        var rewriteOutput = new FakeRewriteOutputStore { FailureMessage = "disk broke" };
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success([new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample;")], WorkspaceLoadMode.SourceOnly, "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            artifactEmissionService: artifactEmission,
            rewriteOutputStore: rewriteOutput);

        var result = await app.RunAsync(new RunRequest("in", "out", Array.Empty<string>(), RunMode.Standard), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
        Assert.Single(artifactEmission.Calls);
        Assert.Contains("disk broke", artifactEmission.Calls[0].Report.FailureSummary!.Message);
    }

    [Fact]
    public async Task RunAsync_ArtifactEmissionFailure_BubblesException()
    {
        var source = new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        var analysisResult = new TestAnalysisContextBuilder().BuildEngineResult(source);
        var artifactEmission = new FakeArtifactEmissionService { FailureMessage = "emit broke" };
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success([source], WorkspaceLoadMode.SourceOnly, "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            artifactEmissionService: artifactEmission);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.RunAsync(new RunRequest("in", "out", Array.Empty<string>(), RunMode.AnalyzeOnly), CancellationToken.None));

        Assert.Equal("emit broke", ex.Message);
    }

    private static AnalysisEngineResult BuildStandardAnalysisResult()
    {
        var memberId = new MemberId("Sample.Player.Update()");
        var target = new PlanTarget("Sample.cs", memberId, MemberKind.Method, TargetKind.Statement, 0, 10, "int count = 1;");
        var source = new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        return new TestAnalysisContextBuilder()
            .AddTarget(new AnalysisTarget(
                target,
                false,
                [new DirectiveAction(PlanActionKind.Delete, null, "dome:delete", "delete")],
                [],
                [],
                [],
                StatementKindRef.Declaration,
                false,
                false,
                false,
                [],
                StatementScopeMode.MinimalBlock,
                "scope",
                null))
            .BuildEngineResult(source);
    }

    private static DomeApplication CreateApplication(
        IWorkspaceLoader workspaceLoader,
        IAnalysisEngine? analysisEngine = null,
        IFunctionImpactAnalyzer? impactAnalyzer = null,
        IReferenceZeroPredictionAnalyzer? predictionAnalyzer = null,
        IRewriteExecutor? rewriteExecutor = null,
        IRewriteOutputStore? rewriteOutputStore = null,
        IArtifactEmissionService? artifactEmissionService = null)
    {
        return new DomeApplication(
            workspaceLoader,
            analysisEngine ?? new FakeAnalysisEngine(new TestAnalysisContextBuilder().BuildEngineResult()),
            impactAnalyzer ?? new FakeFunctionImpactAnalyzer(),
            predictionAnalyzer ?? new FakeReferenceZeroPredictionAnalyzer(),
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            rewriteExecutor ?? new FakeRewriteExecutor(RewriteExecutionResult.Success("namespace Sample;")),
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            new RecordingArtifactWriter(),
            rewriteOutputStore: rewriteOutputStore,
            artifactEmissionService: artifactEmissionService);
    }

    private sealed class FakeWorkspaceLoader(Func<string, Task<WorkspaceLoadResult>> handler) : IWorkspaceLoader
    {
        public Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }
}
