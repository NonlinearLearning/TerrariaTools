using System.Text.Json;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Reporting;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeShadowExtractionApplicationLegacyTests
{
    [Fact]
    public async Task RunAsync_ExtractsReachableDocumentsPreservesNonCodeFilesAndBuildsShadowWorkspace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "TR");
            var outputRoot = Path.Combine(tempRoot, "tr-shadow");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Config"));

            var solutionPath = Path.Combine(sourceRoot, "TerrariaServer.sln");
            await File.WriteAllTextAsync(solutionPath, "solution");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Main.cs"),
                """
                namespace Terraria;

                public static class Main
                {
                    public static void DedServ()
                    {
                        Helper.Run();
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Helper.cs"),
                """
                namespace Terraria;

                internal static class Helper
                {
                    public static void Run()
                    {
                    }

                    public static int HiddenValue()
                    {
                        return 42;
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Unused.cs"),
                """
                namespace Terraria;

                internal static class Unused
                {
                    public static void KeepOut()
                    {
                    }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Config", "settings.json"), "{ }");

            var progress = new FakeTerrariaRuntimeProgressReporter();
            var app = CreateApplication(progress, new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0));
            var result = await app.RunAsync(
                new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(solutionPath, outputRoot, "Terraria.Main.DedServ"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputRoot, "workspace", "Main.cs")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "workspace", "Helper.cs")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "workspace", "Unused.cs")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "workspace", "Config", "settings.json")));

            var helperShadow = await File.ReadAllTextAsync(Path.Combine(outputRoot, "workspace", "Helper.cs"));
            Assert.Contains("public static void Run()", helperShadow);
            Assert.Contains("public static int HiddenValue()", helperShadow);
            Assert.Contains("return default;", helperShadow);
            Assert.DoesNotContain("return 42;", helperShadow);

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputRoot, "artifacts", "shadow-report.json")));
            Assert.Equal("Terraria.Main.DedServ", report.RootElement.GetProperty("SeedMemberName").GetString());
            Assert.Equal(2, report.RootElement.GetProperty("IncludedDocuments").GetArrayLength());
            Assert.Equal(2, report.RootElement.GetProperty("RewrittenDocuments").GetInt32());
            Assert.True(report.RootElement.TryGetProperty("TrBuildSummary", out var buildSummary));
            Assert.True(buildSummary.GetProperty("BuildSucceeded").GetBoolean());
            Assert.Contains(progress.Messages, message => message.Contains("Symbol closure documents", StringComparison.Ordinal));
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
    public async Task RunAsync_DoesNotIncludeTypeDependencyDocumentsOutsideCurrentSymbolClosure()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "TR");
            var outputRoot = Path.Combine(tempRoot, "tr-shadow");
            Directory.CreateDirectory(sourceRoot);

            var solutionPath = Path.Combine(sourceRoot, "TerrariaServer.sln");
            await File.WriteAllTextAsync(solutionPath, "solution");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Main.cs"),
                """
                namespace Terraria;

                public static class Main
                {
                    public static void DedServ()
                    {
                        Helper.Run();
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Helper.cs"),
                """
                namespace Terraria;

                internal static class Helper
                {
                    public static Dependency CreateDependency()
                    {
                        return new Dependency();
                    }

                    public static void Run()
                    {
                        _ = CreateDependency();
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "Dependency.cs"),
                """
                namespace Terraria;

                internal sealed class Dependency
                {
                }
                """);

            var progress = new FakeTerrariaRuntimeProgressReporter();
            var app = CreateApplication(progress, new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0));
            var result = await app.RunAsync(
                new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(solutionPath, outputRoot, "Terraria.Main.DedServ"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(Path.Combine(outputRoot, "workspace", "Dependency.cs")));

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputRoot, "artifacts", "shadow-report.json")));
            Assert.Equal(2, report.RootElement.GetProperty("IncludedDocuments").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static TerrariaRuntimeShadowExtractionApplication CreateApplication(
        ITerrariaRuntimeProgressReporter progressReporter,
        ITerrariaRuntimeBuildExecutor buildExecutor) =>
        new(
            new ShadowExtractionInputResolver(
                new WorkspaceLoadCoordinator(
                    new CodeAnalysisWorkspaceLoader(),
                    new SourceOnlyLoader())),
            new ShadowExtractionAnalysisStage(new RoslynAnalysisEngine()),
            new ShadowClosurePlanner(),
            new ShadowWorkspaceWriter(
                new TerrariaRuntimeShadowProjectBuilder(),
                new TerrariaRuntimeShadowSourceRewriter()),
            buildExecutor,
            new ShadowExtractionReportBuilder(),
            new JsonShadowExtractionReportStore(new JsonArtifactWriter()),
            progressReporter);

    private sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
        }
    }

    private sealed class FakeTerrariaRuntimeBuildExecutor(bool success, int exitCode) : ITerrariaRuntimeBuildExecutor
    {
        public Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(
            ApplicationAbstractions.TerrariaRuntimeLayout layout,
            ITerrariaRuntimeProgressReporter progressReporter,
            CancellationToken cancellationToken)
        {
            progressReporter.Report($"[tr-shadow] dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m");
            return Task.FromResult(new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
                success,
                exitCode,
                $"dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m",
                layout.WorkspacePath,
                layout.DependencyEnvironmentPath,
                layout.WorkspaceSolutionPath,
                string.Empty,
                success ? string.Empty : "shadow build failed"));
        }
    }
}
