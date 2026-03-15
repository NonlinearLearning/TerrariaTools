using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;
using TerrariaTools.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeApplicationSemanticTests
{
    [Fact]
    public async Task RunAsync_ReportsAnalysisFailureWithoutFilesystem()
    {
        var app = CreateApplication(
            workspaceLoader: new FakeWorkspaceLoader(_ => Task.FromResult(CreateUnsupportedAnalysisLoadResult())),
            artifactEmissionService: new FakeArtifactEmissionService());

        var observation = await RunAsync(app, RunMode.Standard);

        Assert.False(observation.Result.IsSuccess);
        Assert.Equal(FailureCode.AnalysisFailed, observation.Result.FailureCode);
        Assert.Equal(FailureCode.AnalysisFailed, observation.Report.FailureSummary?.FailureCode);
    }

    [Fact]
    public async Task RunAsync_BuildsDataflowPropagationPlanWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    // dome:delete
                    int count = 1;
                    int next = count;
                    int final = next;
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        var plan = observation.RequirePlan();
        Assert.Contains(plan.Changes, change => change.Target.DisplayText == "int count = 1;");
        Assert.Contains(plan.Changes, change => change.Target.DisplayText == "int next = count;" && change.Reason.RuleId == "dataflow-propagation");
        Assert.Contains(plan.Changes, change => change.Target.DisplayText == "int final = next;" && change.Reason.RuleId == "dataflow-propagation");
        var propagated = Assert.Single(plan.Changes.Where(change => change.Reason.RuleId == "dataflow-propagation" && change.Target.DisplayText == "int next = count;"));
        Assert.NotNull(propagated.Chain);
        Assert.NotEmpty(propagated.Reason.RelatedSymbolNames ?? Array.Empty<string>());
    }

    [Fact]
    public async Task RunAsync_BuildsStructuredSuccessSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(rewriteExecutor: new FakeRewriteExecutor(RewriteExecutionResult.Success("namespace Sample;"))),
            RunMode.Standard,
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    // dome:delete
                    int count = 1;
                    int next = count;
                    int final = next;
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.Null(observation.Report.FailureSummary);
        Assert.Empty(observation.Report.ConflictSummaries);
        Assert.Equal(0, observation.Report.RiskSummary.SkippedHighRiskTargetCount);
        Assert.Equal(WorkspaceLoadMode.SourceOnly, observation.Report.WorkspaceLoadMode);
        Assert.False(observation.Report.WorkspaceFallbackUsed);
        Assert.True(observation.Report.GeneratedArtifacts.Count >= 2);
    }

    [Fact]
    public async Task RunAsync_BuildsConflictSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.Standard,
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    // dome:delete
                    // dome:comment
                    Run();
                }

                private void Run() { }
            }
            """);

        Assert.False(observation.Result.IsSuccess);
        Assert.Equal(FailureCode.PlanCompileFailed, observation.Result.FailureCode);
        Assert.Equal(FailureCode.PlanCompileFailed, observation.Report.FailureSummary?.FailureCode);
        var conflict = Assert.Single(observation.Report.ConflictSummaries);
        Assert.Equal("MultipleActionsForTarget", conflict.ConflictCode);
    }

    [Fact]
    public async Task RunAsync_BuildsRiskSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public interface IPlayer
            {
                int Value { get; set; }
            }

            public class Player : IPlayer
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set
                    {
                        // dome:delete
                        _value = value;
                    }
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.Equal(1, observation.Report.RiskSummary.SkippedHighRiskTargetCount);
        Assert.NotEmpty(observation.Report.RiskSummary.SampleTargetDisplayTexts);
    }

    [Fact]
    public async Task RunAsync_DoesNotPlanObjectInitializersAndReportsRiskWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Item
            {
                public int Value { get; set; }
            }

            public class Player
            {
                public void Update(int seed)
                {
                    // dome:delete
                    var item = new Item { Value = seed };
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.Empty(observation.RequirePlan().Changes);
        Assert.Equal(1, observation.Report.RiskSummary.SkippedHighRiskTargetCount);
    }

    [Fact]
    public async Task RunAsync_BuildsMethodDeletePlanWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                }

                private void Run()
                {
                }
            }
            """);

        var change = Assert.Single(observation.RequirePlan().Changes, change => change.Target.TargetKind == TargetKind.Method);
        Assert.Equal("function-mark", change.Reason.RuleId);
        Assert.Equal(PlanActionKind.Delete, change.Action.Kind);
        Assert.Equal("Sample.Player.Run()", change.Target.MemberId.Value);
    }

    [Fact]
    public async Task RunAsync_BuildsClassDeletePlanWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                private class CacheEntry
                {
                    public int Value { get; set; }
                }
            }
            """);

        var change = Assert.Single(observation.RequirePlan().Changes, change => change.Target.TargetKind == TargetKind.Class);
        Assert.Equal("class-mark", change.Reason.RuleId);
        Assert.Equal("Sample.Player.CacheEntry", change.Target.MemberId.Value);
    }

    [Fact]
    public async Task RunAsync_BuildsClassDeleteCoverageSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            class CacheEntry
            {
                private void Run()
                {
                    // dome:comment
                    int count = 1;
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.Equal(1, observation.Report.PlanCoverageSummary.CoveredMethodCount);
        Assert.Equal(1, observation.Report.PlanCoverageSummary.CoveredStatementCount);
    }

    [Fact]
    public async Task RunAsync_BuildsFallbackLoaderSummaryWithoutFilesystem()
    {
        const string sourceText = "namespace Sample; public class Player { public void Update() { } }";
        var source = new SourceDocument("Sample.cs", "Sample.cs", sourceText);
        var app = CreateApplication(
            workspaceLoader: new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success(
                [source],
                WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
                "CodeAnalysis",
                true,
                [new WorkspaceLoadDiagnostic("CodeAnalysisLoad", WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")]))));

        var observation = await RunAsync(app, RunMode.PlanOnly);

        Assert.True(observation.Result.IsSuccess);
        Assert.Equal(WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly, observation.Report.WorkspaceLoadMode);
        Assert.True(observation.Report.WorkspaceFallbackUsed);
        Assert.Single(observation.Report.WorkspaceDiagnostics);
    }

    [Fact]
    public async Task RunAsync_BuildsFunctionImpactSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                private void Run()
                {
                    Ping();
                }

                private void Ping()
                {
                }
            }
            """);

        var impact = observation.Report.FunctionImpactSummary;
        Assert.NotNull(impact);
        Assert.Equal(1, impact.DeletedFunctionCount);
        Assert.Equal(1, impact.AffectedFunctionCount);
        Assert.Equal(1, impact.AffectedDocumentCount);
        Assert.Equal(1, impact.ExpansionDepth);
        Assert.Contains(FunctionDependencyKind.Calls, impact.EdgeKinds);
        Assert.Contains("Sample.Player.Ping()", impact.SampleAffectedFunctionIds);
    }

    [Fact]
    public async Task RunAsync_BuildsBoundaryPromotionAndPredictionSummariesWithoutFilesystem()
    {
        var promoted = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    int i = value;
                    int j = i;
                    int k = j;
                    // dome:delete
                    fun2(k);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        var promotion = promoted.Report.BoundaryPromotionSummary;
        Assert.NotNull(promotion);
        Assert.Equal(1, promotion.PromotedMethodDeleteCount);
        Assert.Contains("Sample.Player.fun2(int)", promotion.SamplePromotedMethodIds);
        Assert.Equal(0, promoted.Report.ReferenceZeroPredictionSummary?.PredictedMethodDeleteCount ?? -1);

        var unpredicted = await RunAsync(
            CreateApplication(),
            RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    // dome:delete
                    fun2(value);
                }

                public void Update2(int value)
                {
                    fun2(value);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        Assert.Equal(0, unpredicted.Report.BoundaryPromotionSummary?.PromotedMethodDeleteCount ?? -1);
        Assert.Equal(0, unpredicted.Report.ReferenceZeroPredictionSummary?.PredictedMethodDeleteCount ?? -1);
    }

    private static DomeApplication CreateApplication(
        IWorkspaceLoader? workspaceLoader = null,
        IAnalysisEngine? analysisEngine = null,
        IFunctionImpactAnalyzer? impactAnalyzer = null,
        IReferenceZeroPredictionAnalyzer? predictionAnalyzer = null,
        IRewriteExecutor? rewriteExecutor = null,
        IArtifactEmissionService? artifactEmissionService = null,
        IRewriteOutputStore? rewriteOutputStore = null) =>
        new(
            workspaceLoader ?? new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success(
                [new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }")],
                WorkspaceLoadMode.SourceOnly,
                "StubLoader"))),
            analysisEngine ?? new RoslynAnalysisEngine(),
            impactAnalyzer ?? new FunctionImpactAnalyzer(),
            predictionAnalyzer ?? new ReferenceZeroPredictionAnalyzer(),
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            rewriteExecutor ?? new RoslynRewriteExecutor(),
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            new RecordingArtifactWriter(),
            rewriteOutputStore: rewriteOutputStore ?? new FakeRewriteOutputStore(),
            artifactEmissionService: artifactEmissionService ?? new FakeArtifactEmissionService());

    private static async Task<DomeRunObservation> RunAsync(
        DomeApplication app,
        RunMode mode,
        string? sourceText = null,
        string relativePath = "Sample.cs")
    {
        if (sourceText != null)
        {
            app = CreateApplication(
                workspaceLoader: new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success(
                    [new SourceDocument(relativePath, relativePath, sourceText)],
                    WorkspaceLoadMode.SourceOnly,
                    "StubLoader"))),
                artifactEmissionService: GetArtifactEmission(app),
                rewriteOutputStore: GetRewriteOutput(app));
        }

        var result = await app.RunAsync(new RunRequest("in", "out", Array.Empty<string>(), mode), CancellationToken.None);
        return new DomeRunObservation(result, GetArtifactEmission(app), GetRewriteOutput(app));
    }

    private static FakeArtifactEmissionService GetArtifactEmission(DomeApplication app) =>
        (FakeArtifactEmissionService)typeof(DomeApplication)
            .GetField("_artifactEmissionService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(app)!;

    private static FakeRewriteOutputStore GetRewriteOutput(DomeApplication app) =>
        (FakeRewriteOutputStore)typeof(DomeApplication)
            .GetField("_rewriteOutputStore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(app)!;

    private static WorkspaceLoadResult CreateUnsupportedAnalysisLoadResult()
    {
        var document = new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        return new WorkspaceLoadResult(
            true,
            new UnsupportedAnalysisInput("SampleRoot"),
            [document],
            WorkspaceLoadMode.SourceOnly,
            "StubLoader",
            false,
            Array.Empty<WorkspaceLoadDiagnostic>());
    }

    private sealed class FakeWorkspaceLoader(Func<string, Task<WorkspaceLoadResult>> handler) : IWorkspaceLoader
    {
        public Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }

    private sealed record UnsupportedAnalysisInput(string RootPath) : AnalysisInput(RootPath);

    private sealed record DomeRunObservation(
        RunResult Result,
        FakeArtifactEmissionService ArtifactEmission,
        FakeRewriteOutputStore RewriteOutput)
    {
        public RunReport Report => Assert.Single(ArtifactEmission.Calls).Report;

        public AuditPlan RequirePlan() => Assert.Single(ArtifactEmission.Calls).Plan!;
    }
}
