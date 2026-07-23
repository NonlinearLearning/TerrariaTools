using System.Reflection;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Application;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using RoslynPrototype.Tests.TestCodeSet.DeleteClass;
using RoslynPrototype.Tests.TestCodeSet.Decision;
using RoslynPrototype.Tests.TestCodeSet.Logging;
using RoslynPrototype.Tests.TestCodeSet.Performance;
using RoslynPrototype.Tests.TestCodeSet.Pipeline;
using RoslynPrototype.Tests.TestCodeSet.Propagation;
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
  public void Analyze_AllTestCodeSetSources_BuildsGraphAndRunsApplicationPipeline(TestSourceCase testCase)
  {
    var graph = new RoslynCpgBuilder().BuildFromSource(testCase.Source, testCase.FilePath);
    var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

    var result = application.Analyze(testCase.Source, testCase.FilePath, testCase.Options);

    Assert.NotEmpty(graph.Nodes);
    Assert.NotEmpty(graph.Edges);
    Assert.False(string.IsNullOrWhiteSpace(result.RewrittenSource));
    Assert.All(result.SeedMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
    Assert.All(result.PropagatedMarks, mark => Assert.NotNull(mark.Mark.PrimaryGraphNode));
    Assert.All(result.LiftedMarks, mark => Assert.NotNull(mark.Mark.PrimaryGraphNode));
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
    yield return typeof(CpgBuilderSources);
    yield return typeof(DeleteClassLargeSources);
    yield return typeof(MinimalSources);
    yield return typeof(DecisionComplexSources);
    yield return typeof(ReachabilitySources);
    yield return typeof(RewriteSources);
    yield return typeof(LoggingSources);
    yield return typeof(PerformanceSources);
    yield return typeof(PipelineSources);
    yield return typeof(PropagationSources);
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
    var options = CreateOptions(caseName);

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
    if (caseName.StartsWith("CpgBuilderSources.", StringComparison.Ordinal))
    {
      return 0;
    }

    if (caseName is
        "PerformanceSources.TreeScanSource" or
        "PerformanceSources.CleanupPlayerInputSource" or
        "PerformanceSources.CleanupFirstConsumerSource" or
        "PerformanceSources.CleanupSecondConsumerSource" or
        "PerformanceSources.FirstEmptyNamespaceSource" or
        "PerformanceSources.SecondEmptyNamespaceSource" or
        "PipelineSources.RuntimeConfiguredDopSource" or
        "PipelineSources.ConcurrentMarkingSource" or
        "PipelineSources.RuntimeAwareSource" or
        "PipelineSources.ParallelMarkingSource" or
        "PipelineSources.ParallelPropagationSource" or
        "PipelineSources.OverlappingDeleteSource")
    {
      return 0;
    }

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

  private static IReadOnlyDictionary<string, string> CreateOptions(string caseName)
  {
    if (caseName.StartsWith("DeleteClassLargeSources.", StringComparison.Ordinal) ||
        caseName.StartsWith("PipelineSources.DeleteClass", StringComparison.Ordinal))
    {
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["delete-class"] = "PlayerInput"
      };
    }

    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["target-name"] = ResolveTargetName(caseName)
    };
  }

  private static string ResolveTargetName(string caseName)
  {
    return caseName switch
    {
      "SObjectLogicalSources.LogicalMixedPrecedenceSource" => "b",
      "SObjectLogicalSources.LogicalMixedPrecedenceWithParenthesesSource" => "b",
      "SObjectLogicalSources.LogicalMixedPrecedenceLargeCase1Source" => "b",
      "SObjectLogicalSources.LogicalMixedPrecedenceLargeCase2Source" => "b",
      "SObjectLogicalSources.LogicalMixedPrecedenceLargeCase3Source" => "b",
      "SObjectLogicalSources.LogicalMixedPrecedenceLargeCase4Source" => "b",
      "SObjectLogicalSources.LogicalMixedPrecedenceLargeCase5Source" => "b",
      "SObjectLogicalSources.LogicalMultiTargetGroupFiveHitsSource" => "b,c,d,e,f",
      "SObjectExpressionSources.ConditionalAccessInvokeSource" => "Invoke",
      "SObjectExpressionSources.PropertyAccessDefinitionSource" => "Seed",
      "PipelineSources.RuntimeConfiguredDopSource" => "value",
      "PipelineSources.ConcurrentMarkingSource" => "First",
      "PipelineSources.RuntimeAwareSource" => "RuntimeAware",
      "PipelineSources.ParallelMarkingSource" => "Alpha",
      "PipelineSources.ParallelPropagationSource" => "Alpha",
      "PipelineSources.OverlappingDeleteSource" => "ready",
      "PerformanceSources.TreeScanSource" => "PlayerInput",
      "PerformanceSources.CleanupPlayerInputSource" => "PlayerInput",
      "PerformanceSources.CleanupFirstConsumerSource" => "PlayerInput",
      "PerformanceSources.CleanupSecondConsumerSource" => "PlayerInput",
      "PerformanceSources.FirstEmptyNamespaceSource" => "PlayerInput",
      "PerformanceSources.SecondEmptyNamespaceSource" => "PlayerInput",
      "PropagationSources.ConditionalAccessInvokeSource" => "Invoke",
      "PropagationSources.ObjectCreationWithInitializerSource" => "Seed",
      "PropagationSources.ArgumentShellSource" => "Seed",
      _ => "s"
    };
  }

  private static int GetMinimumDecisions(string caseName)
  {
    return caseName switch
    {
      "ReachabilitySources.NoEntryPointSource" => 0,
      "MinimalSources.EmptyMainSource" => 0,
      "RewriteSources.ReplaceAndDeleteSource" => 0,
      "RewriteSources.NoDecisionSource" => 0,
      var cpgBuilderCase when cpgBuilderCase.StartsWith("CpgBuilderSources.", StringComparison.Ordinal) => 0,
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
