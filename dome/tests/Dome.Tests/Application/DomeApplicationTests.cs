using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;
using Xunit;
using System.Text.Json;

namespace TerrariaTools.Dome.Tests.Application;

/// <summary>
/// Dome 应用程序测试类。
/// </summary>
public class DomeApplicationTests
{
    /// <summary>
    /// 测试运行异步方法将计划和报告写入输出目录。
    /// </summary>
    [Fact]
    public async Task RunAsync_WritesPlanAndReportToOutputDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        // dome:delete
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "Sample.cs")));
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
    public async Task RunAsync_WritesReportWhenAnalysisFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var outputDir = Path.Combine(tempRoot, "out");
            var app = CreateApplication(new FakeWorkspaceLoader(_ => Task.FromResult(CreateUnsupportedAnalysisLoadResult())));

            var result = await app.RunAsync(
                new RunRequest(tempRoot, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureCode.AnalysisFailed, result.FailureCode);
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.Equal("AnalysisFailed", report.RootElement.GetProperty("FailureSummary").GetProperty("FailureCode").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试运行异步方法通过数据流规划依赖语句。
    /// </summary>
    [Fact]
    public async Task RunAsync_PlansDependentStatementsThroughDataflow()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));

            Assert.True(result.IsSuccess);
            Assert.Contains("dataflow-propagation", planJson);
            Assert.Contains("int next = count;", planJson);
            Assert.Contains("int count = 1;", planJson);
            Assert.Contains("int final = next;", planJson);
            Assert.Contains("\"Chain\"", planJson);
            Assert.Contains("\"Hops\"", planJson);
            Assert.Contains("\"Evidence\"", planJson);
            Assert.Contains("RelatedSymbolNames", planJson);
            Assert.Contains("count", planJson);
            Assert.Contains("next", planJson);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试仅分析模式下运行异步方法写入分析和报告但不生成计划或重写。
    /// </summary>
    [Fact]
    public async Task RunAsync_AnalyzeOnly_WritesAnalysisAndReportWithoutPlanOrRewrite()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.AnalyzeOnly),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var analysisPath = Path.Combine(outputDir, "analysis.json");
            Assert.True(File.Exists(analysisPath));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.False(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "rewritten")));

            var analysisJson = await File.ReadAllTextAsync(analysisPath);
            using var analysis = JsonDocument.Parse(analysisJson);
            Assert.True(analysis.RootElement.TryGetProperty("TypeGraph", out _));
            Assert.True(analysis.RootElement.TryGetProperty("FunctionGraph", out _));
            Assert.True(analysis.RootElement.TryGetProperty("StatementGraph", out _));
            Assert.Equal("SnapshotOnly", analysis.RootElement.GetProperty("StatementGraphMaterialization").GetString());
            Assert.Equal("None", analysis.RootElement.GetProperty("FunctionGraphMaterialization").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试仅计划模式下运行异步方法写入计划和报告但不重写。
    /// </summary>
    [Fact]
    public async Task RunAsync_PlanOnly_WritesPlanAndReportWithoutRewrite()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        // dome:delete
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.False(File.Exists(Path.Combine(outputDir, "analysis.json")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "rewritten")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试运行异步方法为成功运行写入结构化报告摘要。
    /// </summary>
    [Fact]
    public async Task RunAsync_WritesStructuredReportSummaryForSuccessfulRun()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.True(report.RootElement.TryGetProperty("GeneratedArtifacts", out var artifacts));
            Assert.True(artifacts.GetArrayLength() >= 2);
            Assert.True(report.RootElement.TryGetProperty("FailureSummary", out var failureSummary));
            Assert.Equal(JsonValueKind.Null, failureSummary.ValueKind);
            Assert.True(report.RootElement.TryGetProperty("ConflictSummaries", out var conflicts));
            Assert.Equal(0, conflicts.GetArrayLength());
            Assert.True(report.RootElement.TryGetProperty("RiskSummary", out var riskSummary));
            Assert.Equal(0, riskSummary.GetProperty("SkippedHighRiskTargetCount").GetInt32());
            Assert.Equal("SourceOnly", report.RootElement.GetProperty("WorkspaceLoadMode").GetString());
            Assert.False(report.RootElement.GetProperty("WorkspaceFallbackUsed").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试运行异步方法为计划编译失败写入冲突摘要。
    /// </summary>
    [Fact]
    public async Task RunAsync_WritesConflictSummaryForPlanCompileFailure()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureCode.PlanCompileFailed, result.FailureCode);
            Assert.True(report.RootElement.TryGetProperty("FailureSummary", out var failureSummary));
            Assert.Equal("PlanCompileFailed", failureSummary.GetProperty("FailureCode").GetString());
            Assert.True(report.RootElement.TryGetProperty("ConflictSummaries", out var conflicts));
            Assert.Equal(1, conflicts.GetArrayLength());
            Assert.Equal("MultipleActionsForTarget", conflicts[0].GetProperty("ConflictCode").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试运行异步方法为受保护的高风险目标写入风险摘要。
    /// </summary>
    [Fact]
    public async Task RunAsync_WritesRiskSummaryForProtectedHighRiskTargets()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.True(report.RootElement.TryGetProperty("RiskSummary", out var riskSummary));
            Assert.Equal(1, riskSummary.GetProperty("SkippedHighRiskTargetCount").GetInt32());
            Assert.True(riskSummary.GetProperty("SampleTargetDisplayTexts").GetArrayLength() >= 1);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试标准模式下运行异步方法为多个文件写入重写输出。
    /// </summary>
    [Fact]
    public async Task RunAsync_StandardMode_WritesRewrittenOutputsForMultipleFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        var inputDir = Path.Combine(tempRoot, "input");
        Directory.CreateDirectory(Path.Combine(inputDir, "Features"));

        try
        {
            var rootFile = Path.Combine(inputDir, "Root.cs");
            var nestedFile = Path.Combine(inputDir, "Features", "Nested.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                rootFile,
                """
                namespace Sample;

                public class RootPlayer
                {
                    public void Update()
                    {
                        // dome:delete
                        Run();
                    }

                    private void Run() { }
                }
                """);
            await File.WriteAllTextAsync(
                nestedFile,
                """
                namespace Sample.Features;

                public class NestedPlayer
                {
                    public void Update()
                    {
                        // dome:comment
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputDir, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var plan = JsonDocument.Parse(planJson);
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "Root.cs")));
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "Features", "Nested.cs")));
            var documentPaths = plan.RootElement
                .GetProperty("Changes")
                .EnumerateArray()
                .Select(change => change.GetProperty("Target").GetProperty("DocumentPath").GetString())
                .OfType<string>()
                .ToArray();
            Assert.Contains("Root.cs", documentPaths);
            Assert.Contains(Path.Combine("Features", "Nested.cs"), documentPaths);
            var generatedArtifacts = report.RootElement
                .GetProperty("GeneratedArtifacts")
                .EnumerateArray()
                .Select(artifact => artifact.GetString())
                .OfType<string>()
                .Select(artifact => artifact.Replace("\\", "/"))
                .ToArray();
            Assert.Contains("rewritten/Root.cs", generatedArtifacts);
            Assert.Contains("rewritten/Features/Nested.cs", generatedArtifacts);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试运行异步方法在重写失败时写入报告。
    /// </summary>
    [Fact]
    public async Task RunAsync_WritesReportWhenRewriteFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        // dome:default
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
            Assert.True(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "rewritten")));
            Assert.Equal("RewriteFailed", report.RootElement.GetProperty("FailureSummary").GetProperty("FailureCode").GetString());
            Assert.Contains("unsupported", report.RootElement.GetProperty("FailureSummary").GetProperty("Message").GetString(), StringComparison.OrdinalIgnoreCase);
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
    public async Task RunAsync_RewriteFailure_PreservesWrittenArtifactsInReport()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var outputDir = Path.Combine(tempRoot, "out");
            var app = CreateApplication(new FakeWorkspaceLoader(_ => Task.FromResult(CreateRewriteFailureLoadResult())));

            var result = await app.RunAsync(
                new RunRequest(tempRoot, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "First.cs")));
            Assert.Contains(
                report.RootElement.GetProperty("GeneratedArtifacts").EnumerateArray().Select(item => item.GetString()).OfType<string>(),
                artifact => string.Equals(artifact, Path.Combine("rewritten", "First.cs"), StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 测试运行异步方法不规划对象初始化器目标并报告风险。
    /// </summary>
    [Fact]
    public async Task RunAsync_DoesNotPlanObjectInitializerTargetsAndReportsRisk()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var plan = JsonDocument.Parse(planJson);
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, plan.RootElement.GetProperty("Changes").GetArrayLength());
            Assert.Equal(1, report.RootElement.GetProperty("RiskSummary").GetProperty("SkippedHighRiskTargetCount").GetInt32());
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
    public async Task RunAsync_ProducesMethodLevelPlanForUnreferencedPrivateMethod()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));

            Assert.True(result.IsSuccess);
            Assert.Contains("\"TargetKind\": \"Method\"", planJson);
            Assert.Contains("\"RuleId\": \"function-mark\"", planJson);
            Assert.Contains("\"Kind\": \"Delete\"", planJson);
            Assert.Contains("Sample.Player.Run()", planJson);
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
    public async Task RunAsync_ProducesClassLevelPlanForUnreferencedNestedClass()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));

            Assert.True(result.IsSuccess);
            Assert.Contains("\"TargetKind\": \"Class\"", planJson);
            Assert.Contains("\"RuleId\": \"class-mark\"", planJson);
            Assert.Contains("Sample.Player.CacheEntry", planJson);
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
    public async Task RunAsync_ProjectsExpressionMarkedStatementsIntoPlan()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public bool Update(int value)
                    {
                        // dome:delete
                        bool allowed = Run(value) && (value > 0);
                        return allowed;
                    }

                    private bool Run(int value) => value > 0;
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            using var plan = JsonDocument.Parse(planJson);

            Assert.True(result.IsSuccess);
            var expressionChange = Assert.Single(plan.RootElement
                .GetProperty("Changes")
                .EnumerateArray()
                .Where(change => change.GetProperty("Reason").GetProperty("RuleId").GetString() == "expression-mark"));
            Assert.Equal("bool allowed = Run(value) && (value > 0);", expressionChange.GetProperty("Target").GetProperty("DisplayText").GetString());
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
    public async Task RunAsync_ClosedLoopDirectDelete_RewritesMarkedStatement()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Player.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public sealed class Player
                {
                    public void Update()
                    {
                        Prepare();

                        // dome:delete
                        int count = 1;

                        Keep();
                    }

                    private static void Prepare()
                    {
                    }

                    private static void Keep()
                    {
                    }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            var rewritten = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Player.cs"));
            using var plan = JsonDocument.Parse(planJson);
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            var change = Assert.Single(plan.RootElement.GetProperty("Changes").EnumerateArray());
            Assert.Equal("Statement", change.GetProperty("Target").GetProperty("TargetKind").GetString());
            Assert.Equal("Delete", change.GetProperty("Action").GetProperty("Kind").GetString());
            Assert.Equal("dome:delete", change.GetProperty("Reason").GetProperty("RuleId").GetString());
            Assert.Equal("int count = 1;", change.GetProperty("Target").GetProperty("DisplayText").GetString());
            Assert.True(report.RootElement.GetProperty("IsSuccess").GetBoolean());
            Assert.Equal(1, report.RootElement.GetProperty("PlannedChanges").GetInt32());
            Assert.Contains(
                report.RootElement.GetProperty("GeneratedArtifacts").EnumerateArray().Select(item => item.GetString()).OfType<string>(),
                item => item == @"rewritten\Player.cs");
            Assert.Contains("Prepare();", rewritten);
            Assert.Contains("Keep();", rewritten);
            Assert.DoesNotContain("int count = 1;", rewritten);
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
    public async Task RunAsync_ExpressionLoop_ProjectsAndPropagatesIntoRewrite()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Player.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public sealed class Player
                {
                    public bool Update(int value)
                    {
                        // dome:delete
                        bool allowed = Run(value) && (value > 0);
                        return allowed;
                    }

                    private static bool Run(int value)
                    {
                        return value > 0;
                    }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            var rewritten = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Player.cs"));
            using var plan = JsonDocument.Parse(planJson);
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            var changes = plan.RootElement.GetProperty("Changes").EnumerateArray().ToArray();
            Assert.Contains(changes, change =>
                change.GetProperty("Reason").GetProperty("RuleId").GetString() == "expression-mark" &&
                change.GetProperty("Target").GetProperty("DisplayText").GetString() == "bool allowed = Run(value) && (value > 0);");
            Assert.Contains(changes, change =>
                change.GetProperty("Reason").GetProperty("RuleId").GetString() == "dataflow-propagation" &&
                change.GetProperty("Target").GetProperty("DisplayText").GetString() == "return allowed;");
            Assert.True(report.RootElement.GetProperty("IsSuccess").GetBoolean());
            Assert.Equal(2, report.RootElement.GetProperty("PlannedChanges").GetInt32());
            Assert.DoesNotContain("bool allowed = Run(value) && (value > 0);", rewritten);
            Assert.DoesNotContain("return allowed;", rewritten);
            Assert.Contains("public bool Update(int value)", rewritten);
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
    public async Task RunAsync_WritesClassDeleteCoverageSummary()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.True(report.RootElement.TryGetProperty("PlanCoverageSummary", out var coverage));
            Assert.Equal(1, coverage.GetProperty("CoveredMethodCount").GetInt32());
            Assert.Equal(1, coverage.GetProperty("CoveredStatementCount").GetInt32());
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
    public async Task RunAsync_WritesFallbackLoaderSummaryWhenCodeAnalysisFallsBack()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                    }
                }
                """);

            var sourceText = await File.ReadAllTextAsync(inputFile);
            var app = new DomeApplication(
                new FakeWorkspaceLoader(_ => Task.FromResult(WorkspaceLoadResult.Success(
                    new[] { new SourceDocument(inputFile, "Sample.cs", sourceText) },
                    WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
                    "CodeAnalysis",
                    true,
                    new[]
                    {
                        new WorkspaceLoadDiagnostic("CodeAnalysisLoad", WorkspaceLoadDiagnosticSeverity.Error, "MSBuild load failed.")
                    }))),
                new RoslynAnalysisEngine(),
                new FunctionImpactAnalyzer(),
                new ReferenceZeroPredictionAnalyzer(),
                new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
                new RoslynRewriteExecutor(),
                new RunReportBuilder(),
                new ArtifactPlanBuilder(),
                new JsonArtifactWriter());

            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.Equal("CodeAnalysisFallbackToSourceOnly", report.RootElement.GetProperty("WorkspaceLoadMode").GetString());
            Assert.True(report.RootElement.GetProperty("WorkspaceFallbackUsed").GetBoolean());
            Assert.Equal(1, report.RootElement.GetProperty("WorkspaceDiagnostics").GetArrayLength());
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
    public async Task RunAsync_WritesFunctionImpactSummaryForMethodDeleteUsingCallsOnly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.True(report.RootElement.TryGetProperty("FunctionImpactSummary", out var impact));
            Assert.Equal(1, impact.GetProperty("DeletedFunctionCount").GetInt32());
            Assert.Equal(1, impact.GetProperty("AffectedFunctionCount").GetInt32());
            Assert.Equal(1, impact.GetProperty("AffectedDocumentCount").GetInt32());
            Assert.Equal(1, impact.GetProperty("ExpansionDepth").GetInt32());
            Assert.Equal("Calls", impact.GetProperty("EdgeKinds")[0].GetString());
            var functionIds = impact.GetProperty("SampleAffectedFunctionIds").EnumerateArray().Select(item => item.GetString()).OfType<string>().ToArray();
            Assert.Contains("Sample.Player.Ping()", functionIds);
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
    public async Task RunAsync_FunctionImpactSummaryIgnoresNonCallEdgesInFirstStage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    private int _value;

                    private int Touch()
                    {
                        return _value;
                    }
                }
                """);

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.True(report.RootElement.TryGetProperty("FunctionImpactSummary", out var impact));
            Assert.Equal(1, impact.GetProperty("DeletedFunctionCount").GetInt32());
            Assert.Equal(0, impact.GetProperty("AffectedFunctionCount").GetInt32());
            Assert.Equal(0, impact.GetProperty("SampleAffectedFunctionIds").GetArrayLength());
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
    public async Task RunAsync_AddsMethodDeleteWhenAllCallReferencesArePlannedForDeletion()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.Contains("\"RuleId\": \"boundary-promotion\"", planJson);
            Assert.Contains("\"TargetKind\": \"Method\"", planJson);
            Assert.Contains("Sample.Player.fun2(int)", planJson);
            Assert.Contains("\"SourceMemberId\": \"Sample.Player.Update(int)\"", planJson);
            Assert.Contains("\"BoundaryKind\": \"Invocation\"", planJson);
            Assert.Contains("\"TriggeredSymbolKeys\": [", planJson);
            Assert.True(report.RootElement.TryGetProperty("BoundaryPromotionSummary", out var promotion));
            Assert.Equal(1, promotion.GetProperty("PromotedMethodDeleteCount").GetInt32());
            Assert.True(report.RootElement.TryGetProperty("ReferenceZeroPredictionSummary", out var prediction));
            Assert.Equal(0, prediction.GetProperty("PredictedMethodDeleteCount").GetInt32());
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
    public async Task RunAsync_DoesNotPredictMethodDeleteWhenOtherCallReferencesRemain()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

            var app = DomeApplicationFactory.CreateDefault();
            var result = await app.RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            var planJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "audit-plan.json"));
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "report.json"));
            using var report = JsonDocument.Parse(reportJson);

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain("\"RuleId\": \"boundary-promotion\"", planJson);
            Assert.DoesNotContain("\"RuleId\": \"reference-zero-prediction\"", planJson);
            Assert.True(report.RootElement.TryGetProperty("BoundaryPromotionSummary", out var promotion));
            Assert.Equal(0, promotion.GetProperty("PromotedMethodDeleteCount").GetInt32());
            Assert.True(report.RootElement.TryGetProperty("ReferenceZeroPredictionSummary", out var prediction));
            Assert.Equal(0, prediction.GetProperty("PredictedMethodDeleteCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeWorkspaceLoader(Func<string, Task<WorkspaceLoadResult>> handler) : IWorkspaceLoader
    {
        public Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
        {
            return handler(inputPath);
        }
    }

    private static DomeApplication CreateApplication(IWorkspaceLoader workspaceLoader) =>
        new(
            workspaceLoader,
            new RoslynAnalysisEngine(),
            new FunctionImpactAnalyzer(),
            new ReferenceZeroPredictionAnalyzer(),
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            new RoslynRewriteExecutor(),
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            new JsonArtifactWriter());

    private static WorkspaceLoadResult CreateUnsupportedAnalysisLoadResult()
    {
        var document = new SourceDocument("Sample.cs", "Sample.cs", "namespace Sample; public class Player { }");
        return new WorkspaceLoadResult(
            true,
            new UnsupportedAnalysisInput("SampleRoot"),
            new[] { document },
            WorkspaceLoadMode.SourceOnly,
            "StubLoader",
            false,
            Array.Empty<WorkspaceLoadDiagnostic>());
    }

    private static WorkspaceLoadResult CreateRewriteFailureLoadResult()
    {
        var first = new SourceDocument(
            Path.Combine("Input", "First.cs"),
            "First.cs",
            """
            namespace Sample;

            public class FirstPlayer
            {
                public void Update()
                {
                    // dome:delete
                    Run();
                }

                private void Run() { }
            }
            """);
        var second = new SourceDocument(
            Path.Combine("Input", "Second.cs"),
            "Second.cs",
            """
            namespace Sample;

            public class SecondPlayer
            {
                public void Update()
                {
                    // dome:default
                    Run();
                }

                private void Run() { }
            }
            """);

        return WorkspaceLoadResult.Success(
            new SourceOnlyAnalysisInput("SampleRoot", new[] { first, second }),
            WorkspaceLoadMode.SourceOnly,
            "StubLoader");
    }

    private sealed record UnsupportedAnalysisInput(string RootPath) : AnalysisInput(RootPath);
}
