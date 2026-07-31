using NLISSN.Application;
using NLISSN.Composition;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DirectoryAnalysisUseCaseTests
{
  [Fact]
  public void Analyze_DeleteUnreferencedMethods_UsesRegisteredRulePipeline()
  {
    var useCase = new DirectoryAnalysisUseCase(RuleRegistry.CreateDefaultRules());
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "InMemory.cs",
        """
        namespace Demo;
        public sealed class Sample
        {
          private static void Removed() { }
          private static void Kept() { }
          public static void Main() { Kept(); }
        }
        """)
    };

    var outcome = useCase.Analyze(
      sources,
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["delete-unreferenced-methods"] = "true"
      },
       AnalysisRuntime.CreateDefault());

    var mark = Assert.Single(
      outcome.Result.SeedMarks,
      mark => string.Equals(mark.RuleId, "DEL-UNREF-METHOD-MARK-001", StringComparison.Ordinal));
    Assert.Equal("UnreferencedMethod", mark.SemanticTag?.Value);
    Assert.NotNull(mark.PrimaryGraphNode);
    var file = Assert.Single(outcome.FileResults);
    Assert.DoesNotContain("Removed", file.Result.RewrittenSource, StringComparison.Ordinal);
    Assert.Contains("Kept", file.Result.RewrittenSource, StringComparison.Ordinal);
    Assert.Contains("Main", file.Result.RewrittenSource, StringComparison.Ordinal);
    Assert.Equal(2, outcome.Result.Stats?.CandidateMethodCount);
    Assert.Equal(1, outcome.Result.Stats?.DeletedMethodCount);
    Assert.Null(outcome.Result.DiffFilePath);
    Assert.Equal("InMemory.cs", Assert.Single(outcome.Result.RewritePlans!).FilePath);
  }
}
