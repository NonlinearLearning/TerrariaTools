using NLISSN.Core.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceFactsTests
{
  [Fact]
  public void CpgFacts_CopyInputCollectionsAndPreserveUnknownValues()
  {
    var passSamples = new List<CpgPassPerformanceFact>
    {
      new("Syntax", 12)
    };
    var cacheCounters = new Dictionary<string, long>(StringComparer.Ordinal)
    {
      ["hit"] = 1
    };
    var facts = new CpgPerformanceFacts(
      "file-a.cs",
      "source-a",
      42,
      passSamples,
      new CpgAnchorDiscoveryFacts(2, 3),
      null,
      cacheCounters,
      Array.Empty<CpgDataFlowMethodPerformanceFact>(),
      10,
      20);

    passSamples.Clear();
    cacheCounters["hit"] = 99;

    Assert.Equal("file-a.cs", facts.ItemId);
    Assert.Equal(42, facts.BuildElapsedMs);
    Assert.Single(facts.PassSamples);
    Assert.Equal(1, facts.CacheCounters["hit"]);
    Assert.Null(facts.Persistence);
  }

  [Fact]
  public void StageSample_KeepsWallAndAccumulatedElapsedSeparate()
  {
    var sample = new PerformanceStageSample(
      "CPG.Build",
      null,
      "file-a.cs",
      12,
      48,
      PerformanceStatus.Completed);

    Assert.Equal(12, sample.WallElapsedMs);
    Assert.Equal(48, sample.AccumulatedElapsedMs);
    Assert.NotEqual(sample.WallElapsedMs, sample.AccumulatedElapsedMs);

    var unknown = sample with
    {
      WallElapsedMs = null,
      AccumulatedElapsedMs = null
    };
    Assert.Null(unknown.WallElapsedMs);
    Assert.Null(unknown.AccumulatedElapsedMs);
  }

  [Fact]
  public void ApplicationFacts_RepresentOnlyCurrentItemAndRunReportOwnsChildren()
  {
    var application = new ApplicationPerformanceFacts(
      "file-a.cs",
      CpgPerformanceFacts.Unavailable("file-a.cs"),
      null,
      null,
      PerformanceStatus.Unavailable);
    var report = RunPerformanceReport.Create(
      "run-1",
      "file",
      application,
      PerformanceStatus.Completed);

    Assert.Equal("file-a.cs", application.ItemId);
    Assert.Null(application.Children);
    Assert.Single(report.Items);
    Assert.Equal("run-1", report.RunId);
    Assert.Equal(PerformanceStatus.Completed, report.TerminalStatus);
  }
}
