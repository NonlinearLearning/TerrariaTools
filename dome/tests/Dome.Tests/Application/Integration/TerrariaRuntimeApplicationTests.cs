using System.Text.Json;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeApplicationTests
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
            var result = await app.RunAsync(new TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputRoot, "artifacts", "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "artifacts", "report.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "workspace", "Config", "settings.json")));

            var rewrittenWorkspace = await File.ReadAllTextAsync(Path.Combine(outputRoot, "workspace", "Player.cs"));
            Assert.DoesNotContain("int count = 1;", rewrittenWorkspace);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputRoot, "artifacts", "report.json"));
            using var report = JsonDocument.Parse(reportJson);
            Assert.True(report.RootElement.TryGetProperty("TrBuildSummary", out var build));
            Assert.True(build.GetProperty("BuildSucceeded").GetBoolean());
            Assert.Equal(0, build.GetProperty("BuildExitCode").GetInt32());
            Assert.Equal(Path.Combine(outputRoot, "workspace"), build.GetProperty("RuntimeWorkspacePath").GetString());
            Assert.Equal(Path.Combine(outputRoot, "dependency-env"), build.GetProperty("DependencyEnvironmentPath").GetString());
            Assert.Contains(progress.Messages, message => message.Contains("dome", StringComparison.Ordinal));
            Assert.Contains(progress.Messages, message => message.Contains("dotnet build", StringComparison.Ordinal));
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
        new(
            CreateDomeApplication(),
            new TerrariaRuntimeEnvironmentBuilder(),
            buildExecutor,
            new JsonRunReportStore(new JsonArtifactWriter()),
            progressReporter,
            new TerrariaRuntimeLayoutFactory());

    private static DomeApplication CreateDomeApplication() =>
        new(
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
            new JsonArtifactWriter());

    private sealed class FakeTerrariaRuntimeBuildExecutor(bool success, int exitCode) : ITerrariaRuntimeBuildExecutor
    {
        public Task<TerrariaRuntimeBuildSummary> ExecuteAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
        {
            progressReporter.Report($"[tr-run] dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m");
            return Task.FromResult(new TerrariaRuntimeBuildSummary(
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
