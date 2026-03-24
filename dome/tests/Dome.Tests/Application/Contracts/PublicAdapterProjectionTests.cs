using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application.Contracts;

public sealed class PublicAdapterCompatibilityProjectionTests
{
    [Fact]
    public async Task RoslynRewriteExecutor_RewritesSingleDocumentSourceSet()
    {
        var executor = new RoslynRewriteExecutor();
        var sourceSet = new ModelAnalysis.SourceDocumentSet(
            "input.cs",
            "root",
            [
                new ModelAnalysis.SourceDocument("input.cs", "input.cs", "class C { void M() { } }")
            ]);
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "input.cs", "out", ModelPrimitives.RunMode.Standard),
            [],
            []);

        var result = await executor.ExecuteAsync(new ModelExecution.RewriteInput(sourceSet, plan), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("void M()", Assert.Single(result.Documents).SourceText);
    }
}




