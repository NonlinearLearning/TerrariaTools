using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using PortsCommon = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Core.Cpg;
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
            Assert.Equal(PortsCommon.WorkspaceLoadMode.SourceOnly, result.LoadMode);
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
                    PortsCommon.WorkspaceLoadMode.CodeAnalysis,
                    "CodeAnalysis",
                    [new ApplicationAbstractions.WorkspaceLoadDiagnostic("CodeAnalysisLoad", PortsCommon.WorkspaceLoadDiagnosticSeverity.Error, "load failed")]))),
                new SourceOnlyLoader());

            var result = await ((ApplicationAbstractions.IWorkspaceLoader)coordinator).LoadAsync(
                projectPath,
                ApplicationAbstractions.WorkspaceLoadOptions.Default,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(PortsCommon.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly, result.LoadMode);
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
        var sourceSet = new ModelAnalysis.SourceDocumentSet(
            "Sample.cs",
            ".",
            [
                new ModelAnalysis.SourceDocument(
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

        Assert.IsType<ModelAnalysis.AnalysisOutput>(result);
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
            new ModelAnalysis.SourceDocumentSet(
                "Sample.cs",
                ".",
                [
                    new ModelAnalysis.SourceDocument(
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
            new ModelAnalysis.SourceDocumentSet(
                "Sample.cs",
                ".",
                [new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", sourceText)]),
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
            analysis.CreateContext(),
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
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                    new ModelPlanning.PlanReason("function-mark", "delete callee"))
            ],
            []);
        var snapshot = analysis.Services.FunctionGraphs.GetSnapshot(
            ApplicationAbstractions.FunctionGraphRequests.ExpandedMembersCalls(
                [new ModelPrimitives.MemberId("Sample.Player.fun2(int)")],
                "AnalysisNativePathTests",
                "function impact validation"));
        var impact = functionImpactAnalyzer.Analyze(plan, analysis);

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

    [Fact]
    public async Task AnalysisEngine_ShouldProjectCallEdgesIntoCodePropertyGraphAndFunctionGraph()
    {
        var engine = (ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new ModelAnalysis.SourceDocumentSet(
                "Sample.cs",
                ".",
                [
                    new ModelAnalysis.SourceDocument(
                        "Sample.cs",
                        "Sample.cs",
                        """
                        class C
                        {
                            void A()
                            {
                            }

                            void B()
                            {
                                A();
                            }
                        }
                        """)
                ]),
            CancellationToken.None);

        Assert.Contains(
            analysis.CodePropertyGraph.Edges,
            edge => edge is
            {
                Label: EdgeKinds.Call,
                SourceId: "method:C.B",
                TargetId: "method:C.A"
            });
        Assert.Contains(
            analysis.View.FunctionGraph.Edges,
            edge => edge.SourceMemberId.Value == "C.B()" &&
                    edge.TargetMemberId.Value == "C.A()" &&
                    edge.Kind == ModelPrimitives.FunctionDependencyKind.Calls);
    }

    [Fact]
    public async Task AnalysisEngine_WorkspaceMode_PreservesQualifiedExternalParameterTypesInMemberIds()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-native-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var externalRoot = Path.Combine(tempRoot, "External");
            var sourceRoot = Path.Combine(tempRoot, "TR");
            Directory.CreateDirectory(externalRoot);
            Directory.CreateDirectory(sourceRoot);

            var externalProjectPath = Path.Combine(externalRoot, "External.csproj");
            var projectPath = Path.Combine(sourceRoot, "TerrariaServer.csproj");

            await File.WriteAllTextAsync(
                externalProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(externalRoot, "ExternalType.cs"),
                """
                namespace External;

                public sealed class ExternalType
                {
                    public void Touch()
                    {
                    }
                }
                """);
            await File.WriteAllTextAsync(
                projectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{Path.GetRelativePath(sourceRoot, externalProjectPath)}}" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Main.cs"),
                """
                using External;

                namespace Terraria;

                public static class Main
                {
                    public static void DedServ()
                    {
                        Helper.Run(new ExternalType());
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Helper.cs"),
                """
                using External;

                namespace Terraria;

                internal static class Helper
                {
                    public static void Run(ExternalType value)
                    {
                        value.Touch();
                    }
                }
                """);

            var loadResult = await new WorkspaceLoadCoordinator(
                    new CodeAnalysisWorkspaceLoader(),
                    new SourceOnlyLoader())
                .LoadAsync(
                    projectPath,
                    ApplicationAbstractions.WorkspaceLoadOptions.Default,
                    CancellationToken.None);

            Assert.True(loadResult.IsSuccess);
            Assert.NotNull(loadResult.Input);
            Assert.Equal(ModelAnalysis.AnalysisInputMode.Workspace, loadResult.Input!.InputMode);

            var analysis = await new RoslynAnalysisEngine().AnalyzeAsync(loadResult.Input, CancellationToken.None);

            Assert.Contains(
                "Terraria.Helper.Run(External.ExternalType)",
                analysis.FunctionIndex.NodesByMemberId.Keys);
            Assert.Contains(
                analysis.Services.MethodCalls.GetReachableMethods([new ModelPrimitives.MemberId("Terraria.Main.DedServ()")]),
                item => item.Value == "Terraria.Helper.Run(External.ExternalType)");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class StubWorkspaceLoader(Func<string, Task<ApplicationAbstractions.WorkspaceLoadResult>> handler) : ApplicationAbstractions.IWorkspaceLoader
    {
        public Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(string inputPath, ApplicationAbstractions.WorkspaceLoadOptions options, CancellationToken cancellationToken) =>
            handler(inputPath);
    }
}
