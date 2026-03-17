using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using LegacyCore = TerrariaTools.Dome.Core;
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
    public void Project_LegacyRunResult_PreservesAllFields()
    {
        var result = LegacyCore.RunResult.Failure(LegacyCore.FailureCode.AnalysisFailed, "out", "boom");

        var projected = DomeTerminalResultProjector.Project(result);

        Assert.False(projected.IsSuccess);
        Assert.Equal(ModelPrimitives.FailureCode.AnalysisFailed, projected.FailureCode);
        Assert.Equal("out", projected.OutputPath);
        Assert.Null(projected.ReportPath);
        Assert.Equal("boom", projected.Message);
    }

    [Fact]
    public void ProjectToLegacy_Success_MapsToLegacySuccess()
    {
        var result = ApplicationAbstractions.RunResult.Success("out", "report.json");

        var projected = DomeTerminalResultProjector.ProjectToLegacy(result);

        Assert.True(projected.IsSuccess);
        Assert.Equal(LegacyCore.FailureCode.None, projected.FailureCode);
        Assert.Equal("out", projected.OutputPath);
        Assert.Equal("report.json", projected.ReportPath);
        Assert.Null(projected.Message);
    }

    [Fact]
    public void ProjectToLegacy_Failure_MapsToLegacyFailure()
    {
        var result = ApplicationAbstractions.RunResult.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "out", "boom");

        var projected = DomeTerminalResultProjector.ProjectToLegacy(result);

        Assert.False(projected.IsSuccess);
        Assert.Equal(LegacyCore.FailureCode.AnalysisFailed, projected.FailureCode);
        Assert.Equal("out", projected.OutputPath);
        Assert.Null(projected.ReportPath);
        Assert.Equal("boom", projected.Message);
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
