using NLISSN.Application;
using NLISSN.Core.Performance;
using NLISSN.Rules;
using RoslynPrototype.Tests.TestCodeSet.Target;
using Xunit;

namespace RoslynPrototype.Tests.Application;

public sealed class ApplicationServicePerformanceTests
{
  [Fact]
  public void Analyze_ExposesCurrentFilePerformanceWithoutChangingBusinessResult()
  {
    var source = AtomicExpressionSources.AtomicNameSource;
    var application = new ApplicationService(RulePipelineTestFactory.Create());

    var result = application.Analyze(
      source,
      "performance.cs",
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["target-name"] = "s"
      });

    var performance = result.Performance;
    Assert.NotNull(performance);
    Assert.Equal("performance.cs", performance.ItemId);
    Assert.NotNull(performance.Cpg);
    Assert.Equal(result.GraphMetrics!.NodeCount, performance.Cpg!.NodeCount);
    Assert.Equal(result.GraphMetrics.EdgeCount, performance.Cpg.EdgeCount);
    Assert.Equal(PerformanceStatus.Completed, performance.Status);
    Assert.Equal(2, result.Decisions.Count);
  }
}
