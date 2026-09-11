using NLISSN.Application.Performance;
using NLISSN.Application;
using NLISSN.Core.Performance;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceResourceAttributionTests
{
  [Fact]
  public void RunMeasurementAttributesProcessCountersToRunAndNotToConcurrentItems()
  {
    var runtime = AnalysisRuntimeFactory.CreateDefault();
    var measurement = new PerformanceRunMeasurement();

    var resources = measurement.CompleteResources(runtime);
    var itemStage = new PerformanceStageSample(
      PerformanceStageId.CpgBuild,
      PerformanceStageId.Run,
      "a.cs",
      1,
      2,
      attribution: PerformanceAttributionLevel.Item);

    Assert.Equal(PerformanceAttributionLevel.Run, resources.Attribution);
    Assert.Null(itemStage.AllocatedBytesDelta);
    Assert.Null(itemStage.WorkingSetBytes);
  }

  [Fact]
  public void MissingTerminalFactsAreNotComplete()
  {
    var report = new RunPerformanceReport(
      "run-1",
      "file",
      "a.cs",
      Array.Empty<ApplicationPerformanceFacts>(),
      null,
      new PerformanceTerminalSummary(null, null, PerformanceStatus.Unknown, false),
      PerformanceStatus.Unknown);

    Assert.False(report.IsComplete);
    Assert.Equal(PerformanceStatus.Unknown, report.TerminalSummary.Status);
  }
}
