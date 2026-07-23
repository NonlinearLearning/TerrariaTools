using System.Text;
using System.Text.Json;
using RoslynPrototype.Application;
using RoslynPrototype.Rewrite;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RewritePlanPersistenceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _tempDirectory;
    private readonly string _inputRoot;
    private readonly RewritePlanArtifactService _artifactService = new();

    public RewritePlanPersistenceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"rewrite-plan-tests-{Guid.NewGuid():N}");
        _inputRoot = Path.Combine(_tempDirectory, "input");
        Directory.CreateDirectory(_inputRoot);
    }

    [Fact]
    public void WriteAndValidate_WithOnePlan_RoundTripsManifestAndPlan()
    {
        var sourcePath = WriteSource("Demo/Sample.cs", "class Sample { int value; }");
        var source = File.ReadAllText(sourcePath);
        var artifactRoot = Path.Combine(_tempDirectory, "artifact");
        var plan = CreatePlan("Demo/Sample.cs", source, new RewritePlanEdit(19, 5, "value", "count"));

        _artifactService.Write(artifactRoot, _inputRoot, sourceFileCount: 1, new[] { plan });
        var (manifest, plans) = _artifactService.ReadAndValidate(artifactRoot, _inputRoot);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("delete-class", manifest.Operation);
        Assert.Equal(Path.GetFullPath(_inputRoot), manifest.InputRoot);
        Assert.Equal(1, manifest.SourceFileCount);
        Assert.Equal(1, manifest.PlannedFileCount);
        Assert.Equal("rewrite-plans.jsonl", manifest.PlanFile);
        Assert.Single(plans);
        Assert.Equal(plan.RelativePath, plans[0].RelativePath);
        Assert.Equal(plan.SourceSha256, plans[0].SourceSha256);
        Assert.Equal(plan.Edits, plans[0].Edits);
    }

    [Fact]
    public void Write_WithUnorderedPlans_ProducesOrdinalJsonlOrdering()
    {
        var zetaPath = WriteSource("Zeta.cs", "class Zeta { }");
        var alphaPath = WriteSource("Alpha.cs", "class Alpha { }");
        var artifactRoot = Path.Combine(_tempDirectory, "artifact");
        var zetaPlan = CreatePlan("Zeta.cs", File.ReadAllText(zetaPath));
        var alphaPlan = CreatePlan("Alpha.cs", File.ReadAllText(alphaPath));

        _artifactService.Write(artifactRoot, _inputRoot, sourceFileCount: 2, new[] { zetaPlan, alphaPlan });

        var records = File.ReadAllLines(Path.Combine(artifactRoot, "rewrite-plans.jsonl"))
            .Select(line => JsonSerializer.Deserialize<RewritePlanFile>(line, JsonOptions))
            .ToArray();
        Assert.Collection(
            records,
            plan => Assert.Equal("Alpha.cs", plan!.RelativePath),
            plan => Assert.Equal("Zeta.cs", plan!.RelativePath));
    }

    [Fact]
    public void ReadAndValidate_WhenPlanDigestDoesNotMatch_ThrowsBeforeReturningPlans()
    {
        var sourcePath = WriteSource("Sample.cs", "class Sample { }");
        var artifactRoot = WriteArtifact(CreatePlan("Sample.cs", File.ReadAllText(sourcePath)));
        var planPath = Path.Combine(artifactRoot, "rewrite-plans.jsonl");
        File.AppendAllText(planPath, " ", new UTF8Encoding(false));

        var exception = Assert.Throws<InvalidOperationException>(
            () => _artifactService.ReadAndValidate(artifactRoot, _inputRoot));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAndValidate_WhenPlanEscapesInputRoot_Throws()
    {
        var sourcePath = WriteSource("Sample.cs", "class Sample { }");
        var artifactRoot = WriteArtifact(CreatePlan("Sample.cs", File.ReadAllText(sourcePath)));
        ReplacePlanAndManifest(artifactRoot, CreatePlan("../outside.cs", "class Outside { }"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => _artifactService.ReadAndValidate(artifactRoot, _inputRoot));

        Assert.Contains("path is invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAndValidate_WhenSourceBytesChange_Throws()
    {
        var sourcePath = WriteSource("Sample.cs", "class Sample { }");
        var artifactRoot = WriteArtifact(CreatePlan("Sample.cs", File.ReadAllText(sourcePath)));
        File.AppendAllText(sourcePath, "// changed", new UTF8Encoding(false));

        var exception = Assert.Throws<InvalidOperationException>(
            () => _artifactService.ReadAndValidate(artifactRoot, _inputRoot));

        Assert.Contains("source SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAndValidate_WhenEditOriginalTextDoesNotMatch_Throws()
    {
        var sourcePath = WriteSource("Sample.cs", "class Sample { int value; }");
        var source = File.ReadAllText(sourcePath);
        var artifactRoot = WriteArtifact(CreatePlan(
            "Sample.cs",
            source,
            new RewritePlanEdit(19, 5, "count", "value")));

        var exception = Assert.Throws<InvalidOperationException>(
            () => _artifactService.ReadAndValidate(artifactRoot, _inputRoot));

        Assert.Contains("edit is invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeDirectory_WithCapturedMultiLineRewritePlan_ReplaysTheSameDiff()
    {
        WriteSource("PlayerInput.cs", """
            namespace Demo;

            public sealed class PlayerInput
            {
            }
            """);
        var artifactRoot = Path.Combine(_tempDirectory, "artifact");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        var captured = await host.AnalyzeFromArgsAsync(new[]
        {
            _inputRoot,
            "--delete-class",
            "PlayerInput",
            "--rewrite-plan-out",
            artifactRoot,
            "--no-diff",
        });
        var replayed = await host.AnalyzeFromArgsAsync(new[]
        {
            _inputRoot,
            "--rewrite-plan-in",
            artifactRoot,
            "--no-diff",
        });

        Assert.NotEmpty(captured.Edits);
        Assert.Equal(captured.Diff.ToString(), replayed.Diff.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteSource(string relativePath, string source)
    {
        var sourcePath = Path.Combine(_inputRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, source, new UTF8Encoding(false));
        return sourcePath;
    }

    private string WriteArtifact(params RewritePlanFile[] plans)
    {
        var artifactRoot = Path.Combine(_tempDirectory, "artifact");
        _artifactService.Write(artifactRoot, _inputRoot, sourceFileCount: plans.Length, plans);
        return artifactRoot;
    }

    private static RewritePlanFile CreatePlan(string relativePath, string source, params RewritePlanEdit[] edits)
    {
        return new RewritePlanFile(
            relativePath,
            RewritePlanArtifactService.ComputeSha256(Encoding.UTF8.GetBytes(source)),
            edits);
    }

    private static void ReplacePlanAndManifest(string artifactRoot, params RewritePlanFile[] plans)
    {
        var planPath = Path.Combine(artifactRoot, "rewrite-plans.jsonl");
        File.WriteAllLines(
            planPath,
            plans.Select(plan => JsonSerializer.Serialize(plan, JsonOptions)),
            new UTF8Encoding(false));
        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        var manifest = JsonSerializer.Deserialize<RewritePlanManifest>(File.ReadAllText(manifestPath), JsonOptions)! with
        {
            PlanSha256 = RewritePlanArtifactService.ComputeSha256(File.ReadAllBytes(planPath))
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
    }
}
