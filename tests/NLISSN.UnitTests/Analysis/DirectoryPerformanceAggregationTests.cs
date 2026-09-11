using NLISSN.Application;
using NLISSN.Core.Performance;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Analysis;

public sealed class DirectoryPerformanceAggregationTests
{
  [Fact]
  public void Aggregate_SortsChildrenAndKeepsDirectoryWallIndependentFromChildSum()
  {
    var fileResults = new[]
    {
      new DirectoryFileAnalysisResult(2, "b.cs", CreateResult("b.cs", 20)),
      new DirectoryFileAnalysisResult(1, "a.cs", CreateResult("a.cs", 10))
    };

    var facts = DirectoryPerformanceFactAggregator.Aggregate(
      "directory",
      fileResults,
      wallElapsedMs: 15);

    Assert.Equal(new[] { "a.cs", "b.cs" }, facts.Children.Select(child => child.ItemId));
    Assert.Equal(2, facts.StageSummary!.Count);
    Assert.Equal(30, facts.StageSummary.SumWallElapsedMs);
    Assert.Equal(20, facts.StageSummary.MaxWallElapsedMs);
    Assert.Equal("b.cs", facts.StageSummary.TopItemId);
    Assert.Equal(15, facts.Stage!.WallElapsedMs);
    Assert.NotEqual(facts.Stage.WallElapsedMs, facts.StageSummary.SumWallElapsedMs);
  }

  [Fact]
  public void Aggregate_EmptyDirectory_ReturnsValidEmptyFacts()
  {
    var facts = DirectoryPerformanceFactAggregator.Aggregate(
      "empty-directory",
      Array.Empty<DirectoryFileAnalysisResult>(),
      wallElapsedMs: 0);

    Assert.Empty(facts.Children);
    Assert.Equal(0, facts.StageSummary!.Count);
    Assert.Equal(0, facts.Stage!.WallElapsedMs);
    Assert.Null(facts.StageSummary.SumWallElapsedMs);
  }

  private static PrototypeAnalysisResult CreateResult(string itemId, long elapsedMs)
  {
    return new PrototypeAnalysisResult(
      Array.Empty<MarkRecord>(),
      Array.Empty<PropagatedMarkRecord>(),
      Array.Empty<LiftedMarkRecord>(),
      Array.Empty<RuleDecision>(),
      Array.Empty<RewriteEdit>(),
      null,
      DiffDocument.Empty,
      null,
      Performance: new ApplicationPerformanceFacts(
        itemId,
        new CpgPerformanceFacts(itemId, "source", elapsedMs),
        null,
        null));
  }
}
