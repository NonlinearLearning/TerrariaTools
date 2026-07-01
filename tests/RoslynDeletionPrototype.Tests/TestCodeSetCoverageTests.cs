using System.Reflection;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Application;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.Decision;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using RoslynPrototype.Tests.TestCodeSet.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TestCodeSetCoverageTests
{
  public static IEnumerable<object[]> AllSourceCases()
  {
    return SourceTypes()
      .SelectMany(EnumerateSourceCases)
      .OrderBy(testCase => testCase.CaseName, StringComparer.Ordinal)
      .Select(testCase => new object[] { testCase });
  }

  [Theory]
  [MemberData(nameof(AllSourceCases))]
  public void Analyze_AllTestCodeSetSources_BuildsGraphAndRunsApplicationPipeline(
    TestSourceCase testCase)
  {
    var graph = new RoslynCpgBuilder().BuildFromSource(testCase.Source, testCase.FilePath);
    var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

    var result = application.Analyze(testCase.Source, testCase.FilePath, testCase.Options);

    Assert.NotEmpty(graph.Nodes);
    Assert.NotEmpty(graph.Edges);
    Assert.False(string.IsNullOrWhiteSpace(result.RewrittenSource));
    Assert.All(result.SeedMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
    Assert.All(result.PropagatedMarks, mark => Assert.NotNull(mark.Mark.PrimaryGraphNode));
    Assert.True(
      result.SeedMarks.Count >= testCase.MinimumSeedMarks,
      $"{testCase.CaseName} expected at least {testCase.MinimumSeedMarks} seed marks.");
    Assert.True(
      result.Decisions.Count >= testCase.MinimumDecisions,
      $"{testCase.CaseName} expected at least {testCase.MinimumDecisions} decisions.");
  }

  private static IEnumerable<Type> SourceTypes()
  {
    yield return typeof(CliInputSources);
    yield return typeof(MinimalSources);
    yield return typeof(DecisionComplexSources);
    yield return typeof(ReachabilitySources);
    yield return typeof(RewriteSources);
    yield return typeof(SObjectControlFlowSources);
    yield return typeof(SObjectExpressionSources);
    yield return typeof(SObjectLogicalSources);
  }

  private static IEnumerable<TestSourceCase> EnumerateSourceCases(Type sourceType)
  {
    foreach (var field in sourceType.GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (!field.IsLiteral || field.FieldType != typeof(string))
      {
        continue;
      }

      var source = (string)field.GetRawConstantValue()!;
      var caseName = $"{sourceType.Name}.{field.Name}";
      yield return CreateSourceCase(caseName, source);
    }
  }

  private static TestSourceCase CreateSourceCase(string caseName, string source)
  {
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["target-name"] = "s"
    };

    var expectedMarks = GetMinimumSeedMarks(caseName);
    var expectedDecisions = GetMinimumDecisions(caseName);
    return new TestSourceCase(
      caseName,
      $"{caseName}.cs",
      source,
      options,
      expectedMarks,
      expectedDecisions);
  }

  private static int GetMinimumSeedMarks(string caseName)
  {
    if (caseName is
        "ReachabilitySources.UnreachableMethodsSource" or
        "ReachabilitySources.NoEntryPointSource" or
        "MinimalSources.EmptyMainSource" or
        "MinimalSources.EmptyMainWithDeadMethodSource")
    {
      return caseName switch
      {
        "ReachabilitySources.UnreachableMethodsSource" => 2,
        "MinimalSources.EmptyMainWithDeadMethodSource" => 1,
        _ => 0
      };
    }

    return caseName.StartsWith("RewriteSources.", StringComparison.Ordinal)
      ? 0
      : 1;
  }

  private static int GetMinimumDecisions(string caseName)
  {
    return caseName switch
    {
      "ReachabilitySources.NoEntryPointSource" => 0,
      "MinimalSources.EmptyMainSource" => 0,
      "RewriteSources.ReplaceAndDeleteSource" => 0,
      "RewriteSources.NoDecisionSource" => 0,
      _ => GetMinimumSeedMarks(caseName)
    };
  }
}

public sealed record TestSourceCase(
  string CaseName,
  string FilePath,
  string Source,
  IReadOnlyDictionary<string, string> Options,
  int MinimumSeedMarks,
  int MinimumDecisions)
{
  public override string ToString()
  {
    return CaseName;
  }
}
