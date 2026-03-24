using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class RewriteExecutorContract
{
    public static async Task AssertReturnsConfiguredResultAsync(ApplicationAbstractions.IRewriteExecutor executor)
    {
        var sourceSet = new CoreAnalysis.SourceDocumentSet(
            "Sample.cs",
            "Sample.cs",
            [new CoreAnalysis.SourceDocument("Sample.cs", "Sample.cs", "class C { void M() { } }")]);
        var plan = new CorePlanning.AuditPlan(
            new CorePlanning.PlanMetadata("dome", "1", "in", "out", CoreCommon.RunMode.Standard),
            Array.Empty<CorePlanning.PlannedChange>(),
            Array.Empty<CorePlanning.PlanConflict>());

        var result = await executor.ExecuteAsync(new ModelExecution.RewriteInput(sourceSet, plan), CancellationToken.None);

        Assert.NotNull(result);
    }
}




