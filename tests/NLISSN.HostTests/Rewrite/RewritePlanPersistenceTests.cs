using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Application;
using NLISSN;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;
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
        var host = new  CommandHost(RuleRegistry.CreateDefaultRules());

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

    [Fact]
    public async Task AnalyzeDirectory_WithCapturedPlan_ReplayedSourcesCompileWithoutErrors()
    {
        WriteSource("PlayerInput.cs", "namespace Demo; public sealed class PlayerInput { }");
        WriteSource("Independent.cs", "namespace Demo; public static class Independent { public static int Run() => 42; }");
        var artifactRoot = Path.Combine(_tempDirectory, "artifact");
        var host = new  CommandHost(RuleRegistry.CreateDefaultRules());

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
        var (_, plans) = _artifactService.ReadAndValidate(artifactRoot, _inputRoot);
        var rewrittenSources = Directory.EnumerateFiles(_inputRoot, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);
        var rewriter = new PrototypeRewriter();

        foreach (var plan in plans)
        {
            var path = Path.Combine(_inputRoot, plan.RelativePath);
            rewrittenSources[path] = Assert.IsType<string>(rewriter.ExecutePlan(
                rewrittenSources[path],
                path,
                plan).RewrittenSource);
        }

        var compilation = CSharpCompilation.Create(
            "ReplayCompilation",
            rewrittenSources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key)),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.NotEmpty(captured.Edits);
        Assert.Equal(captured.Diff.ToString(), replayed.Diff.ToString());
        Assert.Single(plans);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task AnalyzeDirectory_WithMultipleMarkRules_ReplaysEveryManifestFileAndCompiles()
    {
        var targetPath = WriteSource("Target.cs", "namespace Demo; public sealed class Target { }");
        var samplePath = WriteSource("Sample.cs", "namespace Demo; public sealed class Sample { public int Run(int target) { return target; } }");
        var artifactRoot = Path.Combine(_tempDirectory, "artifact");
        var host = new  CommandHost(RuleRegistry.CreateDefaultRules());

        var captured = await host.AnalyzeFromArgsAsync(new[]
        {
            _inputRoot,
            "--delete-class",
            "Target",
            "--target-name",
            "target",
            "--rewrite-plan-out",
            artifactRoot,
            "--no-diff",
        });
        var (manifest, plans) = _artifactService.ReadAndValidate(artifactRoot, _inputRoot);
        var replayed = await host.AnalyzeFromArgsAsync(new[]
        {
            _inputRoot,
            "--rewrite-plan-in",
            artifactRoot,
            "--no-diff",
        });
        var sourcePaths = new[] { targetPath, samplePath };
        var expectedRelativePaths = sourcePaths
            .Select(path => Path.GetRelativePath(_inputRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var planPaths = plans.Select(plan => plan.RelativePath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var rewrittenSources = sourcePaths.ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);
        var rewriter = new PrototypeRewriter();

        foreach (var plan in plans)
        {
            var sourcePath = Path.Combine(_inputRoot, plan.RelativePath);
            rewrittenSources[sourcePath] = Assert.IsType<string>(rewriter.ExecutePlan(
                rewrittenSources[sourcePath],
                sourcePath,
                plan).RewrittenSource);
        }

        var compilation = CSharpCompilation.Create(
            "MultiRuleReplayCompilation",
            rewrittenSources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key)),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Equal(2, manifest.SourceFileCount);
        Assert.Equal(expectedRelativePaths, planPaths);
        Assert.All(plans, plan => Assert.Equal(
            RewritePlanArtifactService.ComputeSha256(File.ReadAllBytes(Path.Combine(_inputRoot, plan.RelativePath))),
            plan.SourceSha256));
        Assert.Equal(captured.Diff.ToString(), replayed.Diff.ToString());
        Assert.NotEmpty(captured.Edits);
        Assert.Empty(errors);
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
