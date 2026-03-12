using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using Xunit;
using System.Text.Json;

namespace TerrariaTools.Dome.Tests.Application;

public class DomeApplicationTests
{
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
            Assert.True(File.Exists(Path.Combine(outputDir, "analysis.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.False(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
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
}
