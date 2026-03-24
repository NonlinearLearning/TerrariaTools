using System.Text.Json;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Runtime.Host;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Adapters.Reporting.Json;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeApplicationLegacyTests
{
    [Fact]
    public async Task RunAsync_WritesArtifactsWorkspaceAndBuildSummary()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "TR");
            var outputRoot = Path.Combine(tempRoot, "tr-runtime");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Config"));

            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.sln"), "solution");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Server.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Player.cs"),
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
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Config", "settings.json"), "{ }");

            var progress = new FakeTerrariaRuntimeProgressReporter();
            var app = CreateApplication(new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0), progress);
            var result = await app.RunAsync(
                new ApplicationAbstractions.TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot),
                CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(result.OutputPath));
            Assert.True(Directory.Exists(outputRoot));
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
    public async Task RunAsync_ReportPreservesCpgFingerprintNotesFromDomePipeline()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "TR");
            var outputRoot = Path.Combine(tempRoot, "tr-runtime");
            Directory.CreateDirectory(sourceRoot);

            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.sln"), "solution");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Server.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Player.cs"),
                """
                namespace Sample;

                public static class Player
                {
                    public static void Update()
                    {
                        Helper();
                    }

                    private static void Helper()
                    {
                    }
                }
                """);

            var app = CreateApplication(new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0), new FakeTerrariaRuntimeProgressReporter());
            var result = await app.RunAsync(
                new ApplicationAbstractions.TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot),
                CancellationToken.None);

            Assert.True(result.IsSuccess);

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputRoot, "artifacts", "report.json")));
            var notes = report.RootElement
                .GetProperty("AdvancedAnalysisSummary")
                .GetProperty("Notes")
                .EnumerateArray()
                .Select(static element => element.GetString())
                .OfType<string>()
                .ToArray();

            Assert.Contains(notes, static note => note.StartsWith("CpgCallEdges=", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static TerrariaRuntimeApplication CreateApplication(ITerrariaRuntimeBuildExecutor buildExecutor, ITerrariaRuntimeProgressReporter progressReporter) =>
        TerrariaRuntimeCompositionRoot.Create(
            new TerrariaRuntimePipelineDependencies(
                CreateDomeApplication(),
                new TerrariaRuntimeEnvironmentBuilder(),
                buildExecutor,
                new JsonRunReportStore(new JsonArtifactWriter()),
                progressReporter,
                new TerrariaRuntimeLayoutFactory()));

    private static DomeApplication CreateDomeApplication() =>
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
                new JsonArtifactWriter()));

    private sealed class FakeTerrariaRuntimeBuildExecutor(bool success, int exitCode) : ITerrariaRuntimeBuildExecutor
    {
        public Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(
            ApplicationAbstractions.TerrariaRuntimeLayout layout,
            ITerrariaRuntimeProgressReporter progressReporter,
            CancellationToken cancellationToken)
        {
            progressReporter.Report($"[tr-run] dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m");
            return Task.FromResult(new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
                success,
                exitCode,
                $"dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m",
                layout.WorkspacePath,
                layout.DependencyEnvironmentPath,
                layout.WorkspaceSolutionPath,
                string.Empty,
                string.Empty));
        }
    }

    private sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
        }
    }
}



