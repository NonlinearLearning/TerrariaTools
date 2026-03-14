using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class ArtifactPlanBuilderTests
{
    [Fact]
    public void BuildAnalysisFailure_WritesOnlyReport()
    {
        var builder = new ArtifactPlanBuilder();

        var plan = builder.BuildAnalysisFailure();

        Assert.False(plan.WriteAnalysis);
        Assert.False(plan.WritePlan);
        Assert.True(plan.WriteReport);
        Assert.Empty(plan.RewrittenDocuments);
        Assert.Equal(new[] { "report.json" }, plan.GeneratedArtifacts);
    }

    [Fact]
    public void BuildAnalyzeOnlySuccess_WritesOnlyAnalysisAndReport()
    {
        var builder = new ArtifactPlanBuilder();

        var plan = builder.BuildAnalyzeOnlySuccess();

        Assert.True(plan.WriteAnalysis);
        Assert.False(plan.WritePlan);
        Assert.True(plan.WriteReport);
        Assert.Empty(plan.RewrittenDocuments);
        Assert.Equal(new[] { "analysis.json", "report.json" }, plan.GeneratedArtifacts);
    }

    [Fact]
    public void BuildRewriteFailure_PreservesPlanAndReportWithoutRewrittenOutputs()
    {
        var builder = new ArtifactPlanBuilder();

        var plan = builder.BuildRewriteFailure(Array.Empty<string>());

        Assert.False(plan.WriteAnalysis);
        Assert.True(plan.WritePlan);
        Assert.True(plan.WriteReport);
        Assert.Empty(plan.RewrittenDocuments);
        Assert.Equal(new[] { "audit-plan.json", "report.json" }, plan.GeneratedArtifacts);
    }

    [Fact]
    public void BuildRewriteFailure_PreservesAlreadyWrittenOutputs()
    {
        var builder = new ArtifactPlanBuilder();

        var plan = builder.BuildRewriteFailure(new[] { Path.Combine("rewritten", "First.cs") });

        Assert.False(plan.WriteAnalysis);
        Assert.True(plan.WritePlan);
        Assert.True(plan.WriteReport);
        Assert.Equal(new[] { Path.Combine("rewritten", "First.cs") }, plan.RewrittenDocuments);
        Assert.Equal(
            new[] { "audit-plan.json", Path.Combine("rewritten", "First.cs"), "report.json" },
            plan.GeneratedArtifacts);
    }
}
