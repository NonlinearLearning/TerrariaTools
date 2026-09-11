using NLISSN.Application;
using NLISSN.Core.Performance;
using RoslynPrototype.Tests.TestCodeSet.Target;
using Xunit;

namespace RoslynPrototype.Tests.Application;

public sealed class DirectoryAnalysisPerformanceTests
{
  [Fact]
  public void Analyze_PreservesStableFilePerformanceFactsAndDirectoryAggregate()
  {
    var sources = new[]
    {
      new DirectorySourceFile(2, "b.cs", AtomicExpressionSources.AtomicNameSource),
      new DirectorySourceFile(1, "a.cs", AtomicExpressionSources.AtomicNameSource)
    };
    var useCase = new DirectoryAnalysisUseCase(RulePipelineTestFactory.Create());

    var outcome = useCase.Analyze(
      sources,
      AnalysisLegacyOptionsTestExtensions.CreateSettings(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          ["target-name"] = "s"
        }),
      AnalysisRuntimeFactory.CreateDefault());

    Assert.Equal(new[] { 1, 2 }, outcome.FileResults.Select(file => file.Index));
    Assert.Equal(new[] { "a.cs", "b.cs" }, outcome.Performance!.Children.Select(child => child.ItemId));
    Assert.Equal(2, outcome.Performance.StageSummary!.Count);
    Assert.Equal(PerformanceStatus.Completed, outcome.Performance.Status);
    Assert.NotNull(outcome.Performance.Stage);
  }
}
