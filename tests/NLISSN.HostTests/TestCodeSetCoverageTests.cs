using System.Reflection;
using NLCPG.Builder;
using NLISSN.Application;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using RoslynPrototype.Tests.TestCodeSet.Large;
using RoslynPrototype.Tests.TestCodeSet.Decision;
using RoslynPrototype.Tests.TestCodeSet.Performance;
using RoslynPrototype.Tests.TestCodeSet.Pipeline;
using RoslynPrototype.Tests.TestCodeSet.Propagation;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using RoslynPrototype.Tests.TestCodeSet.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Target;
using NLISSN.Rules;
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
    var graph = new NLCPGBuilder().BuildFromSource(testCase.Source, testCase.FilePath);
    var application = new ApplicationService(
      RuleRegistry.CreateDefaultRules(
        enableUnreachableMethodDeletion: ShouldEnableUnreachableMethodDeletion(testCase.CaseName)));

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
    yield return typeof(LargeSources);
    yield return typeof(MinimalSources);
    yield return typeof(DecisionComplexSources);
    yield return typeof(ReachabilitySources);
    yield return typeof(RewriteSources);
    yield return typeof(PerformanceSources);
    yield return typeof(PipelineSources);
    yield return typeof(PropagationSources);
    yield return typeof(AtomicControlFlowSources);
    yield return typeof(AtomicExpressionSources);
    yield return typeof(AtomicLogicalSources);
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
    if (caseName.StartsWith("LargeSources.", StringComparison.Ordinal) ||
        caseName is
          "PipelineSources.MethodParameterUsageSource" or
          "PipelineSources.LocalFunctionParameterUsageSource" or
          "PipelineSources.IndexerParameterUsageSource" or
          "PipelineSources.DelegateUsageSource" or
          "PipelineSources.ExtensionMethodUsageSource" or
          "PipelineSources.DeclarationHostSource" or
          "PipelineSources.DeclarationIfStructureCompletionSource")
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

  private static bool ShouldEnableUnreachableMethodDeletion(string caseName)
  {
    return caseName.StartsWith("ReachabilitySources.", StringComparison.Ordinal) ||
      caseName is "MinimalSources.EmptyMainSource" or "MinimalSources.EmptyMainWithDeadMethodSource";
  }

  private static string ResolveTargetName(string caseName)
  {
    return caseName switch
    {
      "AtomicLogicalSources.LogicalMixedPrecedenceSource" => "b",
      "AtomicLogicalSources.LogicalMixedPrecedenceWithParenthesesSource" => "b",
      "AtomicLogicalSources.LogicalMixedPrecedenceLargeCase1Source" => "b",
      "AtomicLogicalSources.LogicalMixedPrecedenceLargeCase2Source" => "b",
      "AtomicLogicalSources.LogicalMixedPrecedenceLargeCase3Source" => "b",
      "AtomicLogicalSources.LogicalMixedPrecedenceLargeCase4Source" => "b",
      "AtomicLogicalSources.LogicalMixedPrecedenceLargeCase5Source" => "b",
      "AtomicLogicalSources.LogicalMultiTargetGroupFiveHitsSource" => "b,c,d,e,f",
      "AtomicExpressionSources.ConditionalAccessInvokeSource" => "Invoke",
      "AtomicExpressionSources.PropertyAccessDefinitionSource" => "Seed",
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
      "AtomicControlFlowSources.DoBodySource" => 0,
      "AtomicControlFlowSources.ForConditionSource" => 0,
      "AtomicControlFlowSources.ForIncrementorSource" => 0,
      "AtomicControlFlowSources.ForInitializerDeclarationSource" => 0,
      "AtomicControlFlowSources.SwitchConditionSource" => 0,
      "AtomicControlFlowSources.WhileBodySource" => 0,
      "AtomicExpressionSources.AssignmentLeftOperandSource" => 0,
      "AtomicExpressionSources.AssignmentStatementSource" => 0,
      "AtomicExpressionSources.ChainedAssignmentStatementSource" => 0,
      "AtomicExpressionSources.ComplexCompoundAssignmentStatementSource" => 0,
      "AtomicExpressionSources.ComplexDefinitionAssignmentSource" => 0,
      "AtomicExpressionSources.ConditionalAccessChainSource" => 0,
      "AtomicExpressionSources.ConditionalAccessPropertySource" => 0,
      "AtomicExpressionSources.ObjectInitializerDefinitionAssignmentSource" => 0,
      "AtomicControlFlowSources.SwitchCaseSingleStatementSource" => 0,
      "AtomicControlFlowSources.SwitchCaseBlockStatementSource" => 0,
      "AtomicControlFlowSources.SwitchCaseMultiStatementSource" => 0,
      "AtomicControlFlowSources.SwitchCaseWithoutBreakSource" => 0,
      "AtomicControlFlowSources.SwitchAllNonDefaultCasesMarkedSource" => 0,
      "CliInputSources.DiffWriteSource" => 0,
      "CliInputSources.ExplicitDiffOutSource" => 0,
      "PropagationSources.ObjectCreationWithInitializerSource" => 0,
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
