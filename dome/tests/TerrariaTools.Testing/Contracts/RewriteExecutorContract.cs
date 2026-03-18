using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class RewriteExecutorContract
{
    public static async Task AssertReturnsConfiguredResultAsync(ApplicationAbstractions.IRewriteExecutor executor)
    {
        var sourceSet = new ApplicationAbstractions.SourceDocumentSet(
            "Sample.cs",
            "Sample.cs",
            [new ApplicationAbstractions.SourceDocument("Sample.cs", "Sample.cs", "class C { void M() { } }")]);
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.Standard),
            Array.Empty<ModelPlanning.PlannedChange>(),
            Array.Empty<ModelPlanning.PlanConflict>());

        var result = await executor.ExecuteAsync(sourceSet, plan, CancellationToken.None);

        Assert.NotNull(result);
    }
}
