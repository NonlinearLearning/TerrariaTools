using NLISSN.Application;
using NLISSN.Core.Analysis;
using NLISSN.Core.Performance;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Application;

public sealed class AnalysisRunOutcomeTests
{
  [Fact]
  public void OutcomeKeepsBusinessResultAndTerminalReportSeparate()
  {
    var result = CreateResult();
    var item = new ApplicationPerformanceFacts(
      "file.cs",
      CpgPerformanceFacts.Unavailable("file.cs"),
      null,
      null,
      PerformanceStatus.Completed);
    var report = RunPerformanceReport.Create("run-1", "file", item, PerformanceStatus.Completed);

    var outcome = new AnalysisRunOutcome(result, report);

    Assert.Same(result, outcome.Result);
    Assert.Same(report, outcome.Performance);
    Assert.Equal("run-1", outcome.Performance.RunId);
    Assert.Same(item, outcome.Performance.Items.Single());
    Assert.Null(outcome.Result.Performance);
  }

  [Fact]
  public void FromDirectoryCarriesChildrenWithoutEmbeddingThemInBusinessResult()
  {
    var result = CreateResult();
    var child = new ApplicationPerformanceFacts(
      "a.cs",
      new CpgPerformanceFacts("a.cs", "source-a", 7),
      null,
      null);
    var directory = new DirectoryPerformanceFacts(
      "directory",
      new[] { child },
      new PerformanceAggregateSummary(1, 1, 7, 7, null, null, "a.cs", 7),
      new PerformanceStageSample("Directory.Analyze", null, "directory", 11, 7));

    var outcome = AnalysisRunOutcome.FromDirectory("run-1", "directory", result, directory);

    Assert.Same(result, outcome.Result);
    Assert.Same(directory, outcome.Performance.Directory);
    Assert.Equal("a.cs", outcome.Performance.Items.Single().ItemId);
    Assert.Equal(11, outcome.Performance.TerminalSummary.WallElapsedMs);
    Assert.Null(outcome.Result.Performance);
  }

  private static PrototypeAnalysisResult CreateResult()
  {
    return new PrototypeAnalysisResult(
      Array.Empty<NLISSN.Core.Marking.MarkRecord>(),
      Array.Empty<NLISSN.Core.Propagation.PropagatedMarkRecord>(),
      Array.Empty<NLISSN.Core.Lifting.LiftedMarkRecord>(),
      Array.Empty<NLISSN.Core.Decision.RuleDecision>(),
      Array.Empty<NLISSN.Core.Rewrite.RewriteEdit>(),
      null,
      DiffDocument.Empty,
      null,
      new AnalysisStats(0, 0, 0, 0));
  }
}
