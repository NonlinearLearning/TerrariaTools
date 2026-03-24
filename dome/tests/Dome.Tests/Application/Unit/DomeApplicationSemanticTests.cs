using System.Runtime.CompilerServices;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using CorePrimitives = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Adapters.Reporting.Json;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Dome.Core.Rules.Services;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using TerrariaTools.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeApplicationSemanticTests
{
    /// <summary>
    /// 保存应用实例与产物发射桩对象的关联。
    /// </summary>
    private static readonly ConditionalWeakTable<DomeApplication, FakeArtifactEmissionService> ArtifactEmissions = new();
    /// <summary>
    /// 保存应用实例与重写输出桩对象的关联。
    /// </summary>
    private static readonly ConditionalWeakTable<DomeApplication, FakeRewriteOutputStore> RewriteOutputs = new();

    [Fact]
    /// <summary>
    /// 验证 RunAsync_ReportsAnalysisFailureWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_ReportsAnalysisFailureWithoutFilesystem()
    {
        var app = CreateApplication(
            analysisEngine: new ThrowingAnalysisEngine("unsupported analysis input"),
            artifactEmissionService: new FakeArtifactEmissionService());

        var observation = await RunAsync(app, ModelPrimitives.RunMode.Standard);

        Assert.False(observation.Result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.AnalysisFailed, observation.Result.FailureCode);
        Assert.Equal(ModelPrimitives.FailureCode.AnalysisFailed, observation.Report.FailureSummary?.FailureCode);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsDataflowPropagationPlanWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsDataflowPropagationPlanWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
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

            public static class Runner
            {
                public static void Run()
                {
                    new Player().Update();
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        var plan = observation.RequirePlan();
        Assert.Contains(plan.Changes, change => change.Locator.DisplayText == "int count = 1;");
        Assert.Contains(plan.Changes, change => change.Locator.DisplayText == "int next = count;" && Assert.IsType<ModelPlanning.PlanReason>(change.Reason).RuleId == "dataflow-propagation");
        Assert.Contains(plan.Changes, change => change.Locator.DisplayText == "int final = next;" && Assert.IsType<ModelPlanning.PlanReason>(change.Reason).RuleId == "dataflow-propagation");
        var propagated = Assert.Single(plan.Changes.Where(change => Assert.IsType<ModelPlanning.PlanReason>(change.Reason).RuleId == "dataflow-propagation" && change.Locator.DisplayText == "int next = count;"));
        Assert.NotNull(propagated.Chain);
        Assert.NotEmpty(Assert.IsType<ModelPlanning.PlanReason>(propagated.Reason).RelatedSymbolNames ?? Array.Empty<string>());
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsStructuredSuccessSummaryWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsStructuredSuccessSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(rewriteExecutor: new FakeRewriteExecutor(ModelExecution.RewriteOutput.Success([new ModelExecution.RewrittenDocument("Sample.cs", "namespace Sample;")]))),
            ModelPrimitives.RunMode.Standard,
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
        Assert.Equal(ModelPrimitives.WorkspaceLoadMode.SourceOnly, observation.Report.WorkspaceLoadMode);
        Assert.False(observation.Report.WorkspaceFallbackUsed);
        Assert.True(observation.Report.GeneratedArtifacts.Count >= 2);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsConflictSummaryWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsConflictSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.Standard,
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

            public static class Runner
            {
                public static void Run()
                {
                    new Player().Update();
                }
            }
            """);

        Assert.False(observation.Result.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.PlanCompileFailed, observation.Result.FailureCode);
        Assert.Equal(ModelPrimitives.FailureCode.PlanCompileFailed, observation.Report.FailureSummary?.FailureCode);
        Assert.Contains(observation.Report.ConflictSummaries, conflict => conflict.ConflictCode == "MultipleActionsForTarget");
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsRiskSummaryWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsRiskSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
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

            public static class Runner
            {
                public static void Run(IPlayer player, int value)
                {
                    player.Value = value;
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.True(observation.Report.RiskSummary.SkippedHighRiskTargetCount >= 0);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_DoesNotPlanObjectInitializersAndReportsRiskWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_DoesNotPlanObjectInitializersAndReportsRiskWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
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

            public static class Runner
            {
                public static void Run()
                {
                    new Player().Update(1);
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.DoesNotContain(observation.RequirePlan().Changes, change => change.Locator.DisplayText.Contains("new Item", StringComparison.Ordinal));
        Assert.True(observation.Report.RiskSummary.SkippedHighRiskTargetCount >= 0);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsMethodDeletePlanWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsMethodDeletePlanWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
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

            public static class Runner
            {
                public static void Run()
                {
                    new Player().Update();
                }
            }
            """);

        var change = Assert.Single(observation.RequirePlan().Changes, change => change.Target.TargetKind == CorePrimitives.TargetKind.Method);
        Assert.Equal("function-mark", Assert.IsType<ModelPlanning.PlanReason>(change.Reason).RuleId);
        Assert.Equal(CorePrimitives.PlanActionKind.Delete, change.Action.Kind);
        Assert.Equal("Sample.Player.Run()", change.Target.MemberId.Value);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsClassDeletePlanWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsClassDeletePlanWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
            """
            namespace Sample;

            public class Player
            {
                private sealed class CacheEntry
                {
                }
            }

            public static class Runner
            {
                public static void Run(Player player)
                {
                    _ = player;
                }
            }
            """);

        var change = Assert.Single(observation.RequirePlan().Changes, change => change.Target.TargetKind == CorePrimitives.TargetKind.Class && change.Target.MemberId.Value == "Sample.Player.CacheEntry");
        Assert.Equal("class-mark", Assert.IsType<ModelPlanning.PlanReason>(change.Reason).RuleId);
        Assert.Equal("Sample.Player.CacheEntry", change.Target.MemberId.Value);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsClassDeleteCoverageSummaryWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsClassDeleteCoverageSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
            """
            namespace Sample;

            class Player
            {
                private sealed class CacheEntry
                {
                    private void Run()
                    {
                        // dome:comment
                        int count = 1;
                    }
                }
            }
            """);

        Assert.True(observation.Result.IsSuccess);
        Assert.Equal(1, observation.Report.PlanCoverageSummary.CoveredMethodCount);
        Assert.Equal(1, observation.Report.PlanCoverageSummary.CoveredStatementCount);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsFallbackLoaderSummaryWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsFallbackLoaderSummaryWithoutFilesystem()
    {
        const string sourceText = "namespace Sample; public class Player { public void Update() { } }";
        var source = new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", sourceText);
        var app = CreateApplication(
            workspaceLoader: new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        source.SourcePath,
                        source.SourcePath,
                        [new ModelAnalysis.SourceDocument(source.SourcePath, source.RelativePath, source.SourceText)]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                ModelPrimitives.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
                "CodeAnalysis",
                true,
                [new ApplicationAbstractions.WorkspaceLoadDiagnostic("CodeAnalysisLoad", ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")]))));

        var observation = await RunAsync(app, ModelPrimitives.RunMode.PlanOnly);

        Assert.True(observation.Result.IsSuccess);
        Assert.Equal(ModelPrimitives.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly, observation.Report.WorkspaceLoadMode);
        Assert.True(observation.Report.WorkspaceFallbackUsed);
        Assert.Single(observation.Report.WorkspaceDiagnostics);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsFunctionImpactSummaryWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsFunctionImpactSummaryWithoutFilesystem()
    {
        var observation = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
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

            public static class Runner
            {
                public static void Run()
                {
                    new Player().Update();
                }
            }
            """);

        var impact = observation.Report.FunctionImpactSummary;
        Assert.NotNull(impact);
        Assert.True(impact.DeletedFunctionCount >= 1);
        Assert.True(impact.AffectedFunctionCount >= 1);
        Assert.True(impact.AffectedDocumentCount >= 1);
        Assert.Contains(TerrariaTools.Dome.Core.Common.FunctionDependencyKind.Calls, impact.EdgeKinds);
        Assert.Contains("Sample.Player.Ping()", impact.SampleAffectedFunctionIds);
    }

    [Fact]
    /// <summary>
    /// 验证 RunAsync_BuildsBoundaryPromotionAndPredictionSummariesWithoutFilesystem。
    /// </summary>
    public async Task RunAsync_BuildsBoundaryPromotionAndPredictionSummariesWithoutFilesystem()
    {
        var promoted = await RunAsync(
            CreateApplication(),
            ModelPrimitives.RunMode.PlanOnly,
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
            ModelPrimitives.RunMode.PlanOnly,
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

    /// <summary>
    /// 创建用于测试的 DomeApplication 实例。
    /// </summary>
    private static DomeApplication CreateApplication(
        ApplicationAbstractions.IWorkspaceLoader? workspaceLoader = null,
        ApplicationAbstractions.IAnalysisEngine? analysisEngine = null,
        ApplicationAbstractions.IFunctionImpactAnalyzer? impactAnalyzer = null,
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer? predictionAnalyzer = null,
        ApplicationAbstractions.IRewriteExecutor? rewriteExecutor = null,
        IArtifactEmissionService? artifactEmissionService = null,
        IRewriteOutputStore? rewriteOutputStore = null)
    {
        var emission = artifactEmissionService as FakeArtifactEmissionService ?? new FakeArtifactEmissionService();
        var rewriteOutput = rewriteOutputStore as FakeRewriteOutputStore ?? new FakeRewriteOutputStore();
        ApplicationAbstractions.IWorkspaceLoader effectiveWorkspaceLoader =
            workspaceLoader ?? new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ModelAnalysis.AnalysisInput(
                    new ModelAnalysis.SourceDocumentSet(
                        "Sample.cs",
                        "Sample.cs",
                        [new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }")]),
                    ModelAnalysis.AnalysisInputMode.SourceOnly),
                ModelPrimitives.WorkspaceLoadMode.SourceOnly,
                "StubLoader")));
        ApplicationAbstractions.IAnalysisEngine effectiveAnalysisEngine =
            analysisEngine ?? (ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine();
        ApplicationAbstractions.IFunctionImpactAnalyzer effectiveImpactAnalyzer =
            impactAnalyzer ?? new FunctionImpactAnalyzer();
        ApplicationAbstractions.IReferenceZeroPredictionAnalyzer effectivePredictionAnalyzer =
            predictionAnalyzer ?? new ReferenceZeroPredictionAnalyzer();
        ApplicationAbstractions.IRewriteExecutor effectiveRewriteExecutor =
            rewriteExecutor ?? new RoslynRewriteExecutor();
        var app = DomeApplicationCompositionRoot.Create(
            new DomePipelineDependencies(
                effectiveWorkspaceLoader,
                effectiveAnalysisEngine,
                effectiveImpactAnalyzer,
                effectivePredictionAnalyzer,
                new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
                effectiveRewriteExecutor,
                new RunReportBuilder(),
                new ArtifactPlanBuilder(),
                new RecordingApplicationArtifactWriter(),
                rewriteOutput,
                emission));
        ArtifactEmissions.Add(app, emission);
        RewriteOutputs.Add(app, rewriteOutput);
        return app;
    }

    /// <summary>
    /// 执行应用并收集观测结果。
    /// </summary>
    private static async Task<DomeRunObservation> RunAsync(
        DomeApplication app,
        ModelPrimitives.RunMode mode,
        string? sourceText = null,
        string relativePath = "Sample.cs")
    {
        if (sourceText != null)
        {
            app = CreateApplication(
                workspaceLoader: new FakeWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Success(
                    new ModelAnalysis.AnalysisInput(
                        new ModelAnalysis.SourceDocumentSet(
                            relativePath,
                            relativePath,
                            [new ModelAnalysis.SourceDocument(relativePath, relativePath, sourceText)]),
                        ModelAnalysis.AnalysisInputMode.SourceOnly),
                    ModelPrimitives.WorkspaceLoadMode.SourceOnly,
                    "StubLoader"))),
                artifactEmissionService: GetArtifactEmission(app),
                rewriteOutputStore: GetRewriteOutput(app));
        }

        var result = await app.RunAsync(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), mode), CancellationToken.None);
        return new DomeRunObservation(result, GetArtifactEmission(app), GetRewriteOutput(app));
    }

    /// <summary>
    /// 获取已登记的产物发射桩对象。
    /// </summary>
    private static FakeArtifactEmissionService GetArtifactEmission(DomeApplication app) =>
        ArtifactEmissions.TryGetValue(app, out var emission)
            ? emission
            : throw new InvalidOperationException("No artifact emission double registered for the DomeApplication instance.");

    /// <summary>
    /// 获取已登记的重写输出桩对象。
    /// </summary>
    private static FakeRewriteOutputStore GetRewriteOutput(DomeApplication app) =>
        RewriteOutputs.TryGetValue(app, out var rewriteOutput)
            ? rewriteOutput
            : throw new InvalidOperationException("No rewrite output double registered for the DomeApplication instance.");

    /// <summary>
    /// 工作区加载器测试桩。
    /// </summary>
    private sealed class FakeWorkspaceLoader(Func<string, Task<ApplicationAbstractions.WorkspaceLoadResult>> handler) : ApplicationAbstractions.IWorkspaceLoader
    {
        /// <summary>
        /// 执行测试桩的加载逻辑。
        /// </summary>
        public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(string inputPath, ApplicationAbstractions.WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }

    /// <summary>
    /// 始终抛出异常的分析引擎测试桩。
    /// </summary>
    private sealed class ThrowingAnalysisEngine(string message) : ApplicationAbstractions.IAnalysisEngine
    {
        /// <summary>
        /// 执行分析并抛出预设异常。
        /// </summary>
        public Task<ModelAnalysis.AnalysisOutput> AnalyzeAsync(
            ModelAnalysis.AnalysisInput input,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// 封装一次运行的测试观测结果。
    /// </summary>
    private sealed record DomeRunObservation(
        ModelExecution.RunResult Result,
        FakeArtifactEmissionService ArtifactEmission,
        FakeRewriteOutputStore RewriteOutput)
    {
        /// <summary>
        /// 获取唯一的运行报告。
        /// </summary>
        public ModelExecution.RunReport Report => Assert.Single(ArtifactEmission.Calls).Report;

        /// <summary>
        /// 获取唯一的计划结果。
        /// </summary>
        public TerrariaTools.Dome.Core.Planning.AuditPlan RequirePlan() => Assert.Single(ArtifactEmission.Calls).Plan!;
    }
}




