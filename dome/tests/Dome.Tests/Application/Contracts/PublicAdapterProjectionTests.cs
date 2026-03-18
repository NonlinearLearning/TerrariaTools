using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Analysis.Legacy;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application.Contracts;

/// <summary>
/// Compatibility coverage for outward adapters on native contracts.
/// These tests are not the standard-path behavior baseline.
/// </summary>
public sealed class PublicAdapterCompatibilityProjectionTests
{
    [Fact]
    public async Task RoslynRewriteExecutor_RewritesSingleDocumentSourceSet()
    {
        var executor = new RoslynRewriteExecutor();
        var sourceSet = new ApplicationAbstractions.SourceDocumentSet(
            "input.cs",
            "root",
            [
                new ApplicationAbstractions.SourceDocument("input.cs", "input.cs", "class C { void M() { } }")
            ]);
        var plan = new TerrariaTools.Dome.Model.Planning.AuditPlan(
            new TerrariaTools.Dome.Model.Planning.PlanMetadata("dome", "1", "input.cs", "out", ModelPrimitives.RunMode.Standard),
            [],
            []);

        var result = await executor.ExecuteAsync(sourceSet, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("void M()", result.RewrittenSource);
    }

    [Fact]
    public void RoslynAnalysisProjection_ProjectResult_IsIdentityForNativeContracts()
    {
        var target = new ModelAnalysis.AnalysisTarget(
            new ModelPrimitives.TargetIdentity("src/Sample.cs", new ModelPrimitives.MemberId("Sample.M"), ModelPrimitives.MemberKind.Method, ModelPrimitives.TargetKind.Statement),
            new ModelPrimitives.TargetLocator(12, 6, "value;"),
            false,
            [],
            [],
            [],
            [],
            ModelPrimitives.StatementKindRef.Assignment,
            false,
            false,
            false,
            [],
            ModelPrimitives.StatementScopeMode.MinimalBlock,
            "scope-1",
            null);

        var result = new ApplicationNativeAnalysisResultBuilder()
            .AddTarget(target)
            .Build(new ApplicationAbstractions.SourceDocument("src/Sample.cs", "src/Sample.cs", "class Sample { void M() { var value = 1; } }"));

        var projected = RoslynAnalysisProjection.ProjectResult(result);

        Assert.Same(result, projected);
        Assert.Single(projected.View.Targets);
        Assert.Equal("value;", projected.View.Targets[0].Locator.DisplayText);
    }
}
