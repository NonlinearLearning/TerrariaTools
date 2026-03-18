using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Analysis.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class AnalysisNativePathTests
{
    [Fact]
    public async Task WorkspaceLoadCoordinator_ApplicationContract_SourceOnlyDirectory_ReturnsModelNativeLoadResult()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-native-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "Sample.cs");
            await File.WriteAllTextAsync(sourcePath, "class Sample { }");
            var coordinator = new WorkspaceLoadCoordinator(
                new StubWorkspaceLoader(_ => throw new InvalidOperationException("CodeAnalysis loader should not run for directory input.")),
                new SourceOnlyLoader());

            var result = await ((ApplicationAbstractions.IWorkspaceLoader)coordinator).LoadAsync(
                tempRoot,
                ApplicationAbstractions.WorkspaceLoadOptions.Default,
                CancellationToken.None);

            Assert.IsType<ApplicationAbstractions.WorkspaceLoadResult>(result);
            Assert.True(result.IsSuccess);
            Assert.Equal(ModelPrimitives.WorkspaceLoadMode.SourceOnly, result.LoadMode);
            Assert.False(result.FallbackUsed);
            Assert.Equal("Sample.cs", Assert.Single(result.Documents).RelativePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WorkspaceLoadCoordinator_ApplicationContract_CodeAnalysisFallback_ReturnsModelNativeLoadResult()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-native-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var projectPath = Path.Combine(tempRoot, "Sample.csproj");
            var sourcePath = Path.Combine(tempRoot, "Sample.cs");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(sourcePath, "class Sample { }");
            var coordinator = new WorkspaceLoadCoordinator(
                new StubWorkspaceLoader(_ => Task.FromResult(ApplicationAbstractions.WorkspaceLoadResult.Failure(
                    ModelPrimitives.WorkspaceLoadMode.CodeAnalysis,
                    "CodeAnalysis",
                    [new ApplicationAbstractions.WorkspaceLoadDiagnostic("CodeAnalysisLoad", ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error, "load failed")]))),
                new SourceOnlyLoader());

            var result = await ((ApplicationAbstractions.IWorkspaceLoader)coordinator).LoadAsync(
                projectPath,
                ApplicationAbstractions.WorkspaceLoadOptions.Default,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(ModelPrimitives.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly, result.LoadMode);
            Assert.True(result.FallbackUsed);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Stage == "CodeAnalysisLoad");
            Assert.Equal("Sample.cs", Assert.Single(result.Documents).RelativePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AnalysisEngine_ApplicationContract_ReturnsModelNativeSnapshotAndStatementFacts()
    {
        var engine = (ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine();
        var sourceSet = new ApplicationAbstractions.SourceDocumentSet(
            "Sample.cs",
            ".",
            [
                new ApplicationAbstractions.SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public class Player
                    {
                        public void Update(int value)
                        {
                            // dome:delete
                            int count = value;
                            int next = count;
                        }
                    }
                    """)
            ]);

        var result = await engine.AnalyzeAsync(sourceSet, CancellationToken.None);

        Assert.IsType<ApplicationAbstractions.AnalysisEngineResult>(result);
        Assert.IsType<ModelAnalysis.AnalysisExecutionSnapshot>(result.Snapshot);
        Assert.IsType<ModelAnalysis.StatementFactsIndex>(result.Snapshot.StatementFacts);
        Assert.NotEmpty(result.Snapshot.StatementFacts.FactsByTargetKey);
        Assert.NotEmpty(result.Snapshot.StatementFacts.FactsByMemberId);
    }

    [Fact]
    public async Task StatementAnalysisService_ApplicationContract_ReturnsModelSnapshotsForBothScopeModes()
    {
        var engine = (ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                ".",
                [
                    new ApplicationAbstractions.SourceDocument(
                        "Sample.cs",
                        "Sample.cs",
                        """
                        namespace Sample;

                        public class Player
                        {
                            public void Update(int seed)
                            {
                                int parent = seed;
                                {
                                    // dome:delete
                                    int child = parent;
                                }
                            }
                        }
                        """)
                ]),
            CancellationToken.None);

        var statementTarget = Assert.Single(result.View.Targets.Where(target =>
            target.Target.TargetKind == ModelPrimitives.TargetKind.Statement &&
            target.Locator.DisplayText.Contains("int child = parent;", StringComparison.Ordinal)));
        var targetKey = $"{statementTarget.Target.IdentityKey}|{statementTarget.Locator.EffectiveResolutionKey.SpanStart}|{statementTarget.Locator.EffectiveResolutionKey.SpanLength}";
        var minimal = result.Services.Statements.Analyze(targetKey, ModelPrimitives.StatementScopeMode.MinimalBlock);
        var parent = result.Services.Statements.Analyze(targetKey, ModelPrimitives.StatementScopeMode.ParentBlockPiercing);

        Assert.IsType<ModelAnalysis.StatementGraphSnapshot>(minimal);
        Assert.IsType<ModelAnalysis.StatementGraphSnapshot>(parent);
        Assert.Equal(ModelPrimitives.StatementScopeMode.MinimalBlock, minimal.ScopeMode);
        Assert.Equal(ModelPrimitives.StatementScopeMode.ParentBlockPiercing, parent.ScopeMode);
        Assert.NotEmpty(minimal.Nodes);
        Assert.NotEmpty(parent.Nodes);
    }

    [Fact]
    public async Task SecondaryAnalyzers_ApplicationContracts_ConsumeModelNativeServicesAndSnapshots()
    {
        var sourceText =
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    // dome:delete
                    fun2(value);
                }

                private void fun2(int i)
                {
                }
            }
            """;
        var engine = (ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                ".",
                [new ApplicationAbstractions.SourceDocument("Sample.cs", "Sample.cs", sourceText)]),
            CancellationToken.None);
        var callStatement = Assert.Single(analysis.View.Targets.Where(target =>
            target.Target.TargetKind == ModelPrimitives.TargetKind.Statement &&
            target.Locator.DisplayText.Contains("fun2(value)", StringComparison.Ordinal)));
        var decisions = new[]
        {
            new ModelRules.MarkDecision(
                callStatement.Target,
                callStatement.Locator,
                new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                new ModelRules.PlanReason("dome:delete", "delete call site"))
        };

        var predictionAnalyzer = (ApplicationAbstractions.IReferenceZeroPredictionAnalyzer)new ReferenceZeroPredictionAnalyzer();
        var predicted = predictionAnalyzer.Predict(
            analysis.Snapshot,
            analysis.Services,
            new ModelRules.RuleExecutionContext("AnalysisNativePathTests", null, ModelPrimitives.StatementScopeMode.MinimalBlock, CancellationToken.None),
            decisions);
        Assert.Contains(predicted, decision =>
            decision.Target.TargetKind == ModelPrimitives.TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.fun2(int)");

        var functionImpactAnalyzer = (ApplicationAbstractions.IFunctionImpactAnalyzer)new FunctionImpactAnalyzer();
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.PlanOnly),
            [
                new ModelPlanning.PlannedChange(
                    0,
                    new ModelPrimitives.TargetIdentity(
                        "Sample.cs",
                        new ModelPrimitives.MemberId("Sample.Player.fun2(int)"),
                        ModelPrimitives.MemberKind.Method,
                        ModelPrimitives.TargetKind.Method),
                    new ModelPrimitives.TargetLocator(0, 0, "fun2"),
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete))
            ],
            []);
        var snapshot = analysis.Services.FunctionGraphs.GetSnapshot(
            ModelAnalysis.FunctionGraphRequests.ExpandedMembersCalls(
                [new ModelPrimitives.MemberId("Sample.Player.fun2(int)")],
                "AnalysisNativePathTests",
                "function impact validation"));
        var impact = functionImpactAnalyzer.Analyze(plan, snapshot);

        Assert.Contains("Sample.Player.fun2(int)", impact.DeletedFunctionIds);

        var reachable = analysis.Services.MethodCalls.GetReachableMethods([new ModelPrimitives.MemberId("Sample.Player.Update(int)")]);
        Assert.Contains(reachable, item => item.Value == "Sample.Player.fun2(int)");

        var callStatementTargetKey =
            $"{callStatement.Target.IdentityKey}|{callStatement.Locator.EffectiveResolutionKey.SpanStart}|{callStatement.Locator.EffectiveResolutionKey.SpanLength}";
        var dataFlow = analysis.Services.DataFlow.Analyze(callStatementTargetKey);
        Assert.Contains("value", dataFlow.Reads);

        var callChain = analysis.Services.CallChains.Analyze("Sample.Player.fun2(int)");
        Assert.Contains(callChain.Entries, entry => entry.MemberId == "Sample.Player.Update(int)");

        var advanced = analysis.Services.AdvancedAnalysis.BuildSummary();
        Assert.True(advanced.PersistentTypeCount >= 1);
    }

    private sealed class StubWorkspaceLoader(Func<string, Task<ApplicationAbstractions.WorkspaceLoadResult>> handler) : ApplicationAbstractions.IWorkspaceLoader
    {
        public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(string inputPath, ApplicationAbstractions.WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }
}
