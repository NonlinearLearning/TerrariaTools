using NLISSN.Application.Performance;
using NLISSN.Core.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceStageTreeTests
{
  [Fact]
  public void StageProtocolContainsStableHostAndAnalysisBoundaries()
  {
    Assert.Contains(PerformanceStageId.WorkspaceLoad, PerformanceStageId.Required);
    Assert.Contains(PerformanceStageId.CpgBuild, PerformanceStageId.Required);
    Assert.Contains(PerformanceStageId.RulePropagate, PerformanceStageId.Required);
    Assert.Contains(PerformanceStageId.ArtifactWriteBack, PerformanceStageId.Required);
    Assert.DoesNotContain(PerformanceStageId.Required, stage => stage.Contains("Service", StringComparison.Ordinal));
  }

  [Fact]
  public void ParentWallTimeIsIndependentFromAccumulatedChildWork()
  {
    using var parent = PerformanceStageScope.Start(PerformanceStageId.CpgBuild);
    using var child = PerformanceStageScope.Start(
      PerformanceStageId.CpgOperation,
      PerformanceStageId.CpgBuild,
      "a.cs");

    var childSample = child.Complete(accumulatedElapsedMs: 12);
    var parentSample = parent.Complete(accumulatedElapsedMs: 12);

    Assert.Equal(PerformanceStageId.CpgBuild, parentSample.StageId);
    Assert.Equal(PerformanceStageId.CpgOperation, childSample.StageId);
    Assert.Equal(PerformanceStageId.CpgBuild, childSample.ParentStageId);
    Assert.Equal(12, parentSample.AccumulatedElapsedMs);
    Assert.NotEqual(parentSample.WallElapsedMs, parentSample.AccumulatedElapsedMs);
  }
}
