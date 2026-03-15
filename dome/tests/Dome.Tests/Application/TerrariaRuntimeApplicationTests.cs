using System.Text.Json;
using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Analysis.Roslyn;
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
            Assert.Contains(progress.Messages, message => message.Contains("开始刷新依赖环境目录", StringComparison.Ordinal));
            Assert.Contains(progress.Messages, message => message.Contains("开始执行 dome 分析、计划和改写", StringComparison.Ordinal));
            Assert.Contains(progress.Messages, message => message.Contains("开始准备运行时工作区", StringComparison.Ordinal));
            Assert.Contains(progress.Messages, message => message.Contains("开始编译解决方案", StringComparison.Ordinal));
            Assert.Contains(progress.Messages, message => message.Contains("TR 专用运行流程已成功完成", StringComparison.Ordinal));
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
    public async Task RunAsync_PreservesWorkspaceAndArtifactsWhenBuildFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "TR");
            var outputRoot = Path.Combine(tempRoot, "tr-runtime");
            Directory.CreateDirectory(sourceRoot);

            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.sln"), "solution");
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

            var progress = new FakeTerrariaRuntimeProgressReporter();
            var app = CreateApplication(new FakeTerrariaRuntimeBuildExecutor(success: false, exitCode: 1), progress);
            var result = await app.RunAsync(new TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot), CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureCode.BuildFailed, result.FailureCode);
            Assert.True(Directory.Exists(Path.Combine(outputRoot, "workspace")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "artifacts", "report.json")));

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputRoot, "artifacts", "report.json"));
            using var report = JsonDocument.Parse(reportJson);
            Assert.True(report.RootElement.TryGetProperty("TrBuildSummary", out var build));
            Assert.False(build.GetProperty("BuildSucceeded").GetBoolean());
            Assert.Equal(1, build.GetProperty("BuildExitCode").GetInt32());
            Assert.Contains(progress.Messages, message => message.Contains("解决方案编译结束，退出码：1", StringComparison.Ordinal));
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
    public async Task RunAsync_UsesSolutionPathForDomeRunRequest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "TR");
            var outputRoot = Path.Combine(tempRoot, "tr-runtime");
            Directory.CreateDirectory(sourceRoot);

            var solutionPath = Path.Combine(sourceRoot, "TerrariaServer.sln");
            var sourceFile = Path.Combine(sourceRoot, "Player.cs");
            await File.WriteAllTextAsync(solutionPath, "solution");
            await File.WriteAllTextAsync(
                sourceFile,
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

            var progress = new FakeTerrariaRuntimeProgressReporter();
            string? capturedInputPath = null;
            var workspaceLoader = new FakeWorkspaceLoader(inputPath =>
            {
                capturedInputPath = inputPath;
                return Task.FromResult(WorkspaceLoadResult.Success(
                    new[] { new SourceDocument(sourceFile, "Player.cs", File.ReadAllText(sourceFile)) },
                    WorkspaceLoadMode.SourceOnly,
                    "StubLoader"));
            });

            var app = CreateApplication(new FakeTerrariaRuntimeBuildExecutor(success: true, exitCode: 0), progress, workspaceLoader);
            var result = await app.RunAsync(new TerrariaRuntimeRunRequest(solutionPath, outputRoot), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(solutionPath, capturedInputPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static TerrariaRuntimeApplication CreateApplication(ITerrariaRuntimeBuildExecutor buildExecutor, ITerrariaRuntimeProgressReporter progressReporter, IWorkspaceLoader? workspaceLoader = null) =>
        new(
            CreateDomeApplication(workspaceLoader),
            new TerrariaRuntimeEnvironmentBuilder(),
            buildExecutor,
            new JsonArtifactWriter(),
            progressReporter);

    private static DomeApplication CreateDomeApplication(IWorkspaceLoader? workspaceLoader = null) =>
        new(
            workspaceLoader ?? new WorkspaceLoadCoordinator(
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
            progressReporter.Report($"[tr-run] 开始编译解决方案：dotnet build \"{layout.WorkspaceSolutionPath}\" --no-restore -m");
            progressReporter.Report($"[tr-run] 解决方案编译结束，退出码：{exitCode}。");
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
        public List<string> Messages { get; } = new();

        public void Report(string message)
        {
            Messages.Add(message);
        }
    }

    private sealed class FakeWorkspaceLoader(Func<string, Task<WorkspaceLoadResult>> handler) : IWorkspaceLoader
    {
        public Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
        {
            return handler(inputPath);
        }
    }
}
