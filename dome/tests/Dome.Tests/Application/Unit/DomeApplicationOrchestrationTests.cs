using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using PortsCommon = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Adapters.Reporting.Json;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Dome.Core.Rules.Services;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using TerrariaTools.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeApplicationOrchestrationTests
{
    [Fact]
    public async Task RunAsync_AnalyzeOnly_EmitsArtifactsWithoutRewriteOutput()
    {
        var source = new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        var analysisResult = new ApplicationNativeAnalysisResultBuilder().Build(source);
        var artifactEmission = new FakeArtifactEmissionService();
        var rewriteOutput = new FakeRewriteOutputStore();
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        source.SourcePath,
                        source.SourcePath,
                        [new ModelAnalysis.SourceDocument(source.SourcePath, source.RelativePath, source.SourceText)]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            artifactEmissionService: artifactEmission,
            rewriteOutputStore: rewriteOutput);

        var result = await app.RunAsync(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), PortsCommon.RunMode.AnalyzeOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(artifactEmission.Calls);
        Assert.True(artifactEmission.Calls[0].ArtifactPlan.WriteAnalysis);
        Assert.Empty(rewriteOutput.Writes);
    }

    [Fact]
    public async Task RunAsync_PlanOnly_MergesInitialAndPredictedDecisionsThroughInjectedAnalyzers()
    {
        var memberId = new ModelPrimitives.MemberId("Sample.Player.Update()");
        var source = new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        var analysisResult = new ApplicationNativeAnalysisResultBuilder()
            .AddTarget(new ModelAnalysis.AnalysisTarget(
                new ModelPrimitives.TargetIdentity("Sample.cs", memberId, ModelPrimitives.MemberKind.Method, ModelPrimitives.TargetKind.Statement),
                new ModelPrimitives.TargetLocator(0, 10, "int count = 1;"),
                false,
                [new ModelAnalysis.DirectiveAction(ModelPrimitives.PlanActionKind.Delete, null, "dome:delete", "delete")],
                [],
                [],
                [],
                ModelPrimitives.StatementKindRef.Declaration,
                false,
                false,
                false,
                [],
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                "scope",
                null))
            .Build(source);
        var artifactEmission = new FakeArtifactEmissionService();
        var predictionTarget = new ModelPrimitives.TargetIdentity(
            "Sample.cs",
            new ModelPrimitives.MemberId("Sample.Player.Run()"),
            ModelPrimitives.MemberKind.Method,
            ModelPrimitives.TargetKind.Method);
        var predictionAnalyzer = new FakeReferenceZeroPredictionAnalyzer(
            new ModelRules.MarkDecision(
                predictionTarget,
                new ModelPrimitives.TargetLocator(20, 5, "Run"),
                new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                new ModelRules.PlanReason(
                    "reference-zero-prediction",
                    "predicted",
                    Origin: ModelPrimitives.DecisionOrigin.Prediction)));
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        source.SourcePath,
                        source.SourcePath,
                        [new ModelAnalysis.SourceDocument(source.SourcePath, source.RelativePath, source.SourceText)]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            predictionAnalyzer: predictionAnalyzer,
            artifactEmissionService: artifactEmission);

        var result = await app.RunAsync(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), PortsCommon.RunMode.PlanOnly), CancellationToken.None);

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
        var rewriteExecutor = new FakeRewriteExecutor(ModelExecution.RewriteOutput.Failure(PortsCommon.FailureCode.RewriteFailed, "rewrite broke"));
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        "Sample.cs",
                        "Sample.cs",
                        [new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample;")]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            rewriteExecutor: rewriteExecutor,
            artifactEmissionService: artifactEmission,
            rewriteOutputStore: rewriteOutput);

        var result = await app.RunAsync(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), PortsCommon.RunMode.Standard), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.RewriteFailed, result.FailureCode);
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
            new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        "Sample.cs",
                        "Sample.cs",
                        [new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample;")]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            artifactEmissionService: artifactEmission,
            rewriteOutputStore: rewriteOutput);

        var result = await app.RunAsync(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), PortsCommon.RunMode.Standard), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.RewriteFailed, result.FailureCode);
        Assert.Single(artifactEmission.Calls);
        Assert.Contains("disk broke", artifactEmission.Calls[0].Report.FailureSummary!.Message);
    }

    [Fact]
    public async Task RunAsync_ArtifactEmissionFailure_BubblesException()
    {
        var source = new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        var analysisResult = new ApplicationNativeAnalysisResultBuilder().Build(source);
        var artifactEmission = new FakeArtifactEmissionService { FailureMessage = "emit broke" };
        var app = CreateApplication(
            new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        source.SourcePath,
                        source.SourcePath,
                        [new ModelAnalysis.SourceDocument(source.SourcePath, source.RelativePath, source.SourceText)]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                PortsCommon.WorkspaceLoadMode.SourceOnly,
                "StubLoader"))),
            new FakeAnalysisEngine(analysisResult),
            artifactEmissionService: artifactEmission);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.RunAsync(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), PortsCommon.RunMode.AnalyzeOnly), CancellationToken.None));

        Assert.Equal("emit broke", ex.Message);
    }

    private static ModelAnalysis.AnalysisOutput BuildStandardAnalysisResult()
    {
        var memberId = new ModelPrimitives.MemberId("Sample.Player.Update()");
        var source = new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        return new ApplicationNativeAnalysisResultBuilder()
            .AddTarget(new ModelAnalysis.AnalysisTarget(
                new ModelPrimitives.TargetIdentity("Sample.cs", memberId, ModelPrimitives.MemberKind.Method, ModelPrimitives.TargetKind.Statement),
                new ModelPrimitives.TargetLocator(0, 10, "int count = 1;"),
                false,
                [new ModelAnalysis.DirectiveAction(ModelPrimitives.PlanActionKind.Delete, null, "dome:delete", "delete")],
                [],
                [],
                [],
                ModelPrimitives.StatementKindRef.Declaration,
                false,
                false,
                false,
                [],
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                "scope",
                null))
            .Build(source);
    }

    private static DomeApplication CreateApplication(
        ApplicationAbstractions.IWorkspaceLoader workspaceLoader,
        ApplicationAbstractions.IAnalysisEngine? analysisEngine = null,
        ApplicationAbstractions.IFunctionImpactAnalyzer? impactAnalyzer = null,
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer? predictionAnalyzer = null,
        ApplicationAbstractions.IRewriteExecutor? rewriteExecutor = null,
        IRewriteOutputStore? rewriteOutputStore = null,
        IArtifactEmissionService? artifactEmissionService = null)
    {
        ApplicationAbstractions.IWorkspaceLoader effectiveWorkspaceLoader = workspaceLoader;
        ApplicationAbstractions.IAnalysisEngine effectiveAnalysisEngine =
            analysisEngine ?? new FakeAnalysisEngine(new ApplicationNativeAnalysisResultBuilder().Build());
        ApplicationAbstractions.IFunctionImpactAnalyzer effectiveImpactAnalyzer =
            impactAnalyzer ?? new FakeFunctionImpactAnalyzer();
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer effectivePredictionAnalyzer =
            predictionAnalyzer ?? new FakeReferenceZeroPredictionAnalyzer();
        ApplicationAbstractions.IRewriteExecutor effectiveRewriteExecutor =
            rewriteExecutor ?? new FakeRewriteExecutor(ModelExecution.RewriteOutput.Success([new ModelExecution.RewrittenDocument("Sample.cs", "namespace Sample;")]));
        ApplicationAbstractions.IArtifactWriter effectiveArtifactWriter =
            new RecordingApplicationArtifactWriter();

        return DomeApplicationCompositionRoot.Create(
            new DomePipelineDependencies(
                effectiveWorkspaceLoader,
                effectiveAnalysisEngine,
                effectiveImpactAnalyzer,
                effectivePredictionAnalyzer,
                new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
                effectiveRewriteExecutor,
                new RunReportBuilder(),
                new ArtifactPlanBuilder(),
                effectiveArtifactWriter,
                rewriteOutputStore,
                artifactEmissionService));
    }

    private sealed class FakeWorkspaceLoader(Func<string, Task<ApplicationAbstractions.WorkspaceLoadResult>> handler) : ApplicationAbstractions.IWorkspaceLoader
    {
        public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(string inputPath, ApplicationAbstractions.WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }
}



