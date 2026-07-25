using Deletion.Application;
using Deletion.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DirectoryAnalysisUseCaseTests
{
  [Fact]
  public void Analyze_DeleteUnreferencedMethods_UsesOnlyInMemorySources()
  {
    var useCase = new DirectoryAnalysisUseCase(new DeletionRulePipeline(
      Array.Empty<RuleDefinitionMark>(),
      Array.Empty<RuleDefinitionPropagate>(),
      Array.Empty<RuleDefinitionLift>(),
      Array.Empty<RuleDefinitionPropose>()));
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "InMemory.cs",
        """
        namespace Demo;
        public sealed class Sample
        {
          private void Removed() { }
          public void Kept() { }
        }
        """)
    };

    var outcome = useCase.Analyze(
      sources,
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["delete-unreferenced-methods"] = "true"
      },
      DeletionAnalysisRuntime.CreateDefault());

    var mark = Assert.Single(outcome.Result.SeedMarks);
    Assert.Equal("DEL-UNREF-METHOD-MARK-001", mark.RuleId);
    var file = Assert.Single(outcome.FileResults);
    Assert.DoesNotContain("Removed", file.Result.RewrittenSource, StringComparison.Ordinal);
    Assert.Contains("Kept", file.Result.RewrittenSource, StringComparison.Ordinal);
    Assert.Null(outcome.Result.DiffFilePath);
    Assert.Equal("InMemory.cs", Assert.Single(outcome.Result.RewritePlans!).FilePath);
  }
}
