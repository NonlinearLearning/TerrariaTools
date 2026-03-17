using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Testing.TestFixtures;
using Xunit;

namespace TerrariaTools.Dome.Tests.Reporting;

public sealed class JsonArtifactWriterTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _directories;

    public JsonArtifactWriterTests(TemporaryDirectoryFixture directories)
    {
        _directories = directories;
    }

    [Fact]
    public async Task WritePlanAsync_CreatesParentDirectory_AndSerializesEnumsAsStrings()
    {
        var writer = new JsonArtifactWriter();
        var path = _directories.GetPath(Path.Combine("plan", "nested", "audit-plan.json"));

        await writer.WritePlanAsync(path, CreatePlan(), CancellationToken.None);

        Assert.True(File.Exists(path));
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"Delete\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAnalysisAsync_CreatesParentDirectory()
    {
        var writer = new JsonArtifactWriter();
        var path = _directories.GetPath(Path.Combine("analysis", "nested", "analysis.json"));

        await writer.WriteAnalysisAsync(path, CreateAnalysis(), CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteReportAsync_CreatesParentDirectory()
    {
        var writer = new JsonArtifactWriter();
        var path = _directories.GetPath(Path.Combine("report", "nested", "report.json"));

        await writer.WriteReportAsync(path, CreateReport(), CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteJsonAsync_WritesArbitraryPayloadUsingSharedSerializerOptions()
    {
        var writer = new JsonArtifactWriter();
        var path = _directories.GetPath(Path.Combine("payload", "nested", "payload.json"));

        await writer.WriteJsonAsync(path, new { Action = ModelPrimitives.PlanActionKind.Delete }, CancellationToken.None);

        Assert.True(File.Exists(path));
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"Delete\"", json, StringComparison.Ordinal);
    }

    private static ModelPlanning.AuditPlan CreatePlan() =>
        new(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.Standard),
            [
                new ModelPlanning.PlannedChange(
                    0,
                    new ModelPrimitives.TargetIdentity("Sample.cs", new ModelPrimitives.MemberId("Sample.Player.Run()"), ModelPrimitives.MemberKind.Method, ModelPrimitives.TargetKind.Method),
                    new ModelPrimitives.TargetLocator(12, 3, "Run"),
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                    new ModelRules.PlanReason("rule", "reason"))
            ],
            []);

    private static ModelAnalysis.AnalysisResultModel CreateAnalysis() =>
        new(
            [],
            [],
            new ModelAnalysis.TypeDependencyGraph([], []),
            new ModelAnalysis.FunctionDependencyGraph([], []),
            new ModelAnalysis.StatementDependencyGraph([], []),
            ModelAnalysis.StatementGraphMaterialization.SnapshotOnly,
            ModelAnalysis.FunctionGraphMaterialization.None);

    private static ApplicationAbstractions.RunReport CreateReport() =>
        new(
            true,
            ModelPrimitives.FailureCode.None,
            1,
            1,
            0,
            0,
            ["audit-plan.json"],
            null,
            [],
            new ApplicationAbstractions.RiskSummary(0, []),
            new ApplicationAbstractions.PlanCoverageSummary(1, 0, ["Run"]),
            null,
            null,
            null,
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            false,
            [],
            null);
}
