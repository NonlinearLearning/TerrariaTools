using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Application;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeTerminalResultProjectorTests
{
    [Fact]
    public void Project_ApplicationRunResult_ReturnsEquivalentResult()
    {
        var result = ApplicationAbstractions.RunResult.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "out", "boom");

        var projected = DomeTerminalResultProjector.Project(result);

        Assert.Equal(result, projected);
    }

    [Fact]
    public void SuccessAndFailure_CreateExpectedApplicationResults()
    {
        var success = DomeTerminalResultProjector.Success("out", "report.json");
        var failure = DomeTerminalResultProjector.Failure(ModelPrimitives.FailureCode.RewriteFailed, "out", "rewrite broke");

        Assert.True(success.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.None, success.FailureCode);
        Assert.Equal("report.json", success.ReportPath);

        Assert.False(failure.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.RewriteFailed, failure.FailureCode);
        Assert.Equal("out", failure.OutputPath);
        Assert.Equal("rewrite broke", failure.Message);
    }
}
