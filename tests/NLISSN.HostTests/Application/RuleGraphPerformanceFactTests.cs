using NLISSN.Application.Performance;
using NLISSN.Core.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Application;

public sealed class RuleGraphPerformanceFactTests
{
  [Fact]
  public void Map_SortsTelemetryByStableNodeIdAndPreservesStatusAndPeaks()
  {
    var telemetry = new[]
    {
      new RuleGraphNodeTelemetry(
        RuleNodeId.For(RuleKind.Propose, "z-rule"),
        4,
        5,
        6),
      new RuleGraphNodeTelemetry(
        RuleNodeId.For(RuleKind.Mark, "a-rule"),
        1,
        2,
        3,
        RuleGraphNodeStatus.Disabled)
    };

    var facts = RuleGraphPerformanceFactMapper.Map(
      telemetry,
      new RuleGraphExecutionMetrics(7, 8));

    Assert.NotNull(facts);
    var result = facts!;
    Assert.Equal(new[] { "Mark:a-rule", "Propose:z-rule" }, result.NodeSamples.Select(node => node.NodeId));
    Assert.Equal(PerformanceStatus.Skipped, result.NodeSamples[0].Status);
    Assert.Equal(2, result.NodeSamples[0].OutputCount);
    Assert.Equal(6, result.NodeSamples[1].WallElapsedMs);
    Assert.Equal(7, result.PeakReadyNodeCount);
    Assert.Equal(8, result.PeakConcurrentNodeCount);
  }

  [Fact]
  public void Map_WhenNoRuleGraphFactsExist_ReturnsNull()
  {
    var facts = RuleGraphPerformanceFactMapper.Map(null, null);

    Assert.Null(facts);
  }
}
