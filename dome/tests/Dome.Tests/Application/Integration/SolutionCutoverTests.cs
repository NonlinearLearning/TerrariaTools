using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Adapters.Reporting.Json;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.Runtime.Host;
using TerrariaTools.Dome.Application.ShadowExtraction.Host;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class SolutionCutoverTests
{
    [Fact]
    public void SolutionCutover_CustomizedDomeRecipe_PreservesFixedStageTopology()
    {
        var recipe = DomeFlowRecipes.Standard(
                new DomeFlowSlots(
                    new StubLoadSlot(),
                    new SpecialAnalyzeSlotAdapter(new StubSpecialAnalyzeEngine()),
                    new StubRuleSlot(),
                    new StubDecisionSlot(),
                    new StubResultSlot()))
            .DecorateResult(inner => new DecoratingResultSlot(inner));

        var stages = DomeFlowRecipes.BuildStages(
            recipe,
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            new NullArtifactEmissionService(),
            new FakeDomeProgressReporter());

        Assert.Equal(["LoadSlotStage", "AnalyzeSlotStage", "RuleSlotStage", "DecisionSlotStage", "ResultSlotStage"], stages.Select(stage => stage.GetType().Name).ToArray());
    }

    [Fact]
    public async Task SolutionCutover_ShouldPassAnalyzePlanRunAndShadowFlows()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var standardInputPath = Path.Combine(tempRoot, "Standard.cs");
            var analyzeOutputPath = Path.Combine(tempRoot, "analyze-out");
            var planOutputPath = Path.Combine(tempRoot, "plan-out");
            var runOutputPath = Path.Combine(tempRoot, "run-out");

            await File.WriteAllTextAsync(
                standardInputPath,
                """
                namespace Sample;

                public static class EntryPoint
                {
                    public static void Run()
                    {
                        // dome:delete
                        Helper();
                    }

                    private static void Helper()
                    {
                    }
                }
                """);

            var standardApp = DomeApplicationFactory.CreateDefault();

            var analyzeResult = await standardApp.RunAsync(
                new ApplicationAbstractions.RunRequest(
                    standardInputPath,
                    analyzeOutputPath,
                    Array.Empty<string>(),
                    ModelPrimitives.RunMode.AnalyzeOnly),
                CancellationToken.None);
            var planResult = await standardApp.RunAsync(
                new ApplicationAbstractions.RunRequest(
                    standardInputPath,
                    planOutputPath,
                    Array.Empty<string>(),
                    ModelPrimitives.RunMode.PlanOnly),
                CancellationToken.None);
            var runResult = await standardApp.RunAsync(
                new ApplicationAbstractions.RunRequest(
                    standardInputPath,
                    runOutputPath,
                    Array.Empty<string>(),
                    ModelPrimitives.RunMode.Standard),
                CancellationToken.None);

            Assert.True(analyzeResult.IsSuccess);
            Assert.True(File.Exists(Path.Combine(analyzeOutputPath, "analysis.json")));
            Assert.True(File.Exists(Path.Combine(analyzeOutputPath, "report.json")));
            Assert.True(planResult.IsSuccess);
            Assert.True(File.Exists(Path.Combine(planOutputPath, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(planOutputPath, "report.json")));
            Assert.True(runResult.IsSuccess);
            Assert.True(File.Exists(Path.Combine(runOutputPath, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(runOutputPath, "report.json")));
            Assert.True(File.Exists(Path.Combine(runOutputPath, "rewritten", "Standard.cs")));

            var runtimeSourceRoot = Path.Combine(tempRoot, "Runtime");
            var runtimeOutputRoot = Path.Combine(tempRoot, "tr-runtime");
            Directory.CreateDirectory(Path.Combine(runtimeSourceRoot, "Config"));

            await File.WriteAllTextAsync(Path.Combine(runtimeSourceRoot, "TerrariaServer.sln"), "solution");
            await File.WriteAllTextAsync(Path.Combine(runtimeSourceRoot, "Server.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(runtimeSourceRoot, "Player.cs"),
                """
                namespace Sample;

                public sealed class Player
                {
                    public void Update()
                    {
                        // dome:delete
                        int count = 1;
                    }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(runtimeSourceRoot, "Config", "settings.json"), "{ }");

            var runtimeResult = await CreateRuntimeApplication().RunAsync(
                new ApplicationAbstractions.TerrariaRuntimeRunRequest(
                    Path.Combine(runtimeSourceRoot, "TerrariaServer.sln"),
                    runtimeOutputRoot),
                CancellationToken.None);

            Assert.True(runtimeResult.IsSuccess);
            Assert.True(File.Exists(Path.Combine(runtimeOutputRoot, "artifacts", "report.json")));

            var externalRoot = Path.Combine(tempRoot, "External");
            var shadowSourceRoot = Path.Combine(tempRoot, "Shadow");
            var shadowOutputRoot = Path.Combine(tempRoot, "tr-shadow");
            Directory.CreateDirectory(externalRoot);
            Directory.CreateDirectory(shadowSourceRoot);

            var externalProjectPath = Path.Combine(externalRoot, "External.csproj");
            var shadowProjectPath = Path.Combine(shadowSourceRoot, "TerrariaServer.csproj");

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
                shadowProjectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{Path.GetRelativePath(shadowSourceRoot, externalProjectPath)}}" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(shadowSourceRoot, "Main.cs"),
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
                Path.Combine(shadowSourceRoot, "Helper.cs"),
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

            var shadowResult = await CreateShadowApplication().RunAsync(
                new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(
                    shadowProjectPath,
                    shadowOutputRoot,
                    "Terraria.Main.DedServ"),
                CancellationToken.None);

            Assert.True(shadowResult.IsSuccess);
            Assert.True(File.Exists(Path.Combine(shadowOutputRoot, "artifacts", "shadow-report.json")));

            var helperShadow = await File.ReadAllTextAsync(Path.Combine(shadowOutputRoot, "workspace", "Helper.cs"));
            Assert.Contains("value.Touch();", helperShadow);
            Assert.DoesNotContain("return default;", helperShadow, StringComparison.Ordinal);
        });
    }

    private static TerrariaRuntimeApplication CreateRuntimeApplication() =>
        TerrariaRuntimeCompositionRoot.Create(
            new TerrariaRuntimePipelineDependencies(
                DomeApplicationCompositionRoot.Create(
                    new DomePipelineDependencies(
                        new WorkspaceLoadCoordinator(
                            new CodeAnalysisWorkspaceLoader(),
                            new SourceOnlyLoader()),
                        new RoslynAnalysisEngine(),
                        new FunctionImpactAnalyzer(),
                        new ReferenceZeroPredictionAnalyzer(),
                        new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
                        new RoslynRewriteExecutor(),
                        new RunReportBuilder(),
                        new ArtifactPlanBuilder(),
                        new JsonArtifactWriter())),
                new TerrariaRuntimeEnvironmentBuilder(),
                new FakeBuildExecutor(),
                new JsonRunReportStore(new JsonArtifactWriter()),
                new FakeProgressReporter(),
                new TerrariaRuntimeLayoutFactory()));

    private static TerrariaRuntimeShadowExtractionApplication CreateShadowApplication() =>
        TerrariaRuntimeShadowExtractionCompositionRoot.Create(
            new ShadowExtractionPipelineDependencies(
                new ShadowExtractionInputResolver(
                    new WorkspaceLoadCoordinator(
                        new CodeAnalysisWorkspaceLoader(),
                        new SourceOnlyLoader()),
                    new TerrariaRuntimeShadowLayoutFactory()),
                new ShadowExtractionAnalysisStage(new RoslynAnalysisEngine()),
                new ShadowClosurePlanner(new SeedClosureAnalyzer()),
                new ShadowWorkspaceWriter(
                    new TerrariaRuntimeShadowProjectBuilder(),
                    new TerrariaRuntimeShadowSourceRewriter()),
                new FakeBuildExecutor(),
                new ShadowExtractionReportBuilder(),
                new JsonShadowExtractionReportStore(new JsonArtifactWriter()),
                new FakeProgressReporter()));

    private static async Task WithTempRootAsync(Func<string, Task> action)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            await action(tempRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public void Report(string message)
        {
        }
    }

    private sealed class FakeDomeProgressReporter : IDomeProgressReporter
    {
        public void Report(string message)
        {
        }
    }

    private sealed class FakeBuildExecutor : ITerrariaRuntimeBuildExecutor
    {
        public Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(
            ApplicationAbstractions.TerrariaRuntimeLayout layout,
            ITerrariaRuntimeProgressReporter progressReporter,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
                true,
                0,
                $"dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m",
                layout.WorkspacePath,
                layout.DependencyEnvironmentPath,
                layout.WorkspaceSolutionPath,
                string.Empty,
                string.Empty));
        }
    }

    private sealed class NullArtifactEmissionService : IArtifactEmissionService
    {
        public Task EmitAsync(
            string outputPath,
            ArtifactPlan artifactPlan,
            TerrariaTools.Dome.Core.Planning.AuditPlan? plan,
            ModelExecution.RunReport report,
            TerrariaTools.Dome.Core.Analysis.AnalysisResultModel? analysisView,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubLoadSlot : ApplicationAbstractions.ILoadSlot
    {
        public Task<ApplicationAbstractions.LoadOutput> ExecuteAsync(
            ApplicationAbstractions.LoadInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRuleSlot : ApplicationAbstractions.IRuleSlot
    {
        public Task<ApplicationAbstractions.RuleOutput> ExecuteAsync(
            ApplicationAbstractions.RuleInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubDecisionSlot : ApplicationAbstractions.IDecisionSlot
    {
        public Task<ApplicationAbstractions.DecisionOutput> ExecuteAsync(
            ApplicationAbstractions.DecisionInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubResultSlot : ApplicationAbstractions.IResultSlot
    {
        public Task<ModelExecution.RunResult> ExecuteAsync(
            ApplicationAbstractions.ResultInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private interface ISpecialAnalyzeEngine
    {
        Task<SpecialAnalyzeResponse> ExecuteAsync(
            SpecialAnalyzeRequest request,
            CancellationToken cancellationToken);
    }

    private sealed record SpecialAnalyzeRequest(string InputPath, int DocumentCount);

    private sealed record SpecialAnalyzeResponse(ApplicationAbstractions.AnalyzeOutput Output);

    private sealed class StubSpecialAnalyzeEngine : ISpecialAnalyzeEngine
    {
        public Task<SpecialAnalyzeResponse> ExecuteAsync(
            SpecialAnalyzeRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SpecialAnalyzeSlotAdapter(ISpecialAnalyzeEngine inner) : ApplicationAbstractions.IAnalyzeSlot
    {
        public async Task<ApplicationAbstractions.AnalyzeOutput> ExecuteAsync(
            ApplicationAbstractions.AnalyzeInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken)
        {
            var request = new SpecialAnalyzeRequest(
                input.Load.Request.InputPath,
                input.Load.Workspace.Documents.Count);
            var response = await inner.ExecuteAsync(request, cancellationToken);
            return response.Output;
        }
    }

    private sealed class DecoratingResultSlot(ApplicationAbstractions.IResultSlot inner) : ApplicationAbstractions.IResultSlot
    {
        public Task<ModelExecution.RunResult> ExecuteAsync(
            ApplicationAbstractions.ResultInput input,
            ApplicationAbstractions.IFlowExecutionContext executionContext,
            CancellationToken cancellationToken) => inner.ExecuteAsync(input, executionContext, cancellationToken);
    }
}
