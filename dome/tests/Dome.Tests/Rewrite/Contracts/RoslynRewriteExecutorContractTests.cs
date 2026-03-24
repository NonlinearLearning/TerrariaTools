using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Testing.Contracts;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rewrite;

public sealed class RoslynRewriteExecutorContractTests
{
    [Fact]
    public async Task Executor_SatisfiesRewriteContract()
    {
        await RewriteExecutorContract.AssertReturnsConfiguredResultAsync(new RoslynRewriteExecutor());
    }

    [Fact]
    public async Task Executor_SucceedsForEmptyPlan()
    {
        var sourceSet = new ModelAnalysis.SourceDocumentSet(
            "Sample.cs",
            "Sample.cs",
            [new ModelAnalysis.SourceDocument("Sample.cs", "Sample.cs", "class C { void M() { } }")]);
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.Standard),
            Array.Empty<ModelPlanning.PlannedChange>(),
            Array.Empty<ModelPlanning.PlanConflict>());

        var result = await new RoslynRewriteExecutor().ExecuteAsync(sourceSet, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("void M()", result.RewrittenSource);
    }
}
