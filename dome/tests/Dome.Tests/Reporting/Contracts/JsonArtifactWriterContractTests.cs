using TerrariaTools.Dome.Reporting;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Testing.Contracts;
using TerrariaTools.Testing.TestFixtures;
using Xunit;

namespace TerrariaTools.Dome.Tests.Reporting;

public sealed class JsonArtifactWriterContractTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _directories;

    public JsonArtifactWriterContractTests(TemporaryDirectoryFixture directories)
    {
        _directories = directories;
    }

    [Fact]
    public async Task Writer_SatisfiesArtifactWriterContract()
    {
        await ArtifactWriterContract.AssertWritesArtifactsAsync(new JsonArtifactWriter());
    }

    [Fact]
    public async Task Writer_CreatesExpectedArtifactFiles()
    {
        var writer = new JsonArtifactWriter();
        var root = _directories.CreateDirectory("json-artifacts");

        await ArtifactWriterContract.AssertWritesArtifactsAsyncAtPath(writer, root);

        Assert.True(File.Exists(Path.Combine(root, "analysis.json")));
        Assert.True(File.Exists(Path.Combine(root, "audit-plan.json")));
        Assert.True(File.Exists(Path.Combine(root, "report.json")));
    }

    [Fact]
    public async Task Writer_EmitsModelNativePlanShape()
    {
        var writer = new JsonArtifactWriter();
        var root = _directories.CreateDirectory("json-model-shape");
        var planPath = Path.Combine(root, "audit-plan.json");
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.Standard),
            new[]
            {
                new ModelPlanning.PlannedChange(
                    0,
                    new ModelPrimitives.TargetIdentity(
                        "Sample.cs",
                        new ModelPrimitives.MemberId("Sample.Player.Run()"),
                        ModelPrimitives.MemberKind.Method,
                        ModelPrimitives.TargetKind.Method),
                    new ModelPrimitives.TargetLocator(12, 7, "Run();"),
                    new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                    new ModelRules.PlanReason("rule", "reason"))
            },
            Array.Empty<ModelPlanning.PlanConflict>());

        await writer.WritePlanAsync(planPath, plan, CancellationToken.None);

        var json = await File.ReadAllTextAsync(planPath);
        Assert.Contains("\"target\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"locator\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"displayText\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
