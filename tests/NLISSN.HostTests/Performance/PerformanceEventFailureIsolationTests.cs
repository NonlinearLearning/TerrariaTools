using NLISSN.Core.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceEventFailureIsolationTests
{
  [Fact]
  public void ThrowingEventSinkDoesNotChangeTerminalFactPopulation()
  {
    var item = new ApplicationPerformanceFacts(
      "a.cs",
      CpgPerformanceFacts.Unavailable("a.cs"),
      null,
      null);
    var report = RunPerformanceReport.Create("run-1", "file", item, PerformanceStatus.Completed);

    Assert.True(report.IsComplete);
    Assert.Empty(report.Attachments);
  }
}
