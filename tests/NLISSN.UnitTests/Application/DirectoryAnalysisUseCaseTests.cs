using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Application;
using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DirectoryAnalysisUseCaseTests
{
  [Fact]
  public void Analyze_DeleteUnreferencedMethods_UsesRegisteredRulePipeline()
  {
    var useCase = new DirectoryAnalysisUseCase(
      RuleRegistry.CreateDefaultRules(enableUnreferencedMethodDeletion: true));
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
      mark => string.Equals(mark.RuleId, "mark.unreferenced-method", StringComparison.Ordinal));
    Assert.Equal(RuleFactKind.UnreferencedMethod, mark.FactKind);
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

  [Fact]
  public void Analyze_DeleteUnreferencedMethods_WhenCompilationHasErrors_PreservesPrivateMethods()
  {
    var useCase = new DirectoryAnalysisUseCase(
      RuleRegistry.CreateDefaultRules(enableUnreferencedMethodDeletion: true));
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "InMemory.cs",
        """
        namespace Demo;

        public sealed class Sample
        {
          public void Run(MissingType value)
          {
            Used(value);
          }

          private void Used(MissingType value) { }

          private void Dead() { }
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

    var file = Assert.Single(outcome.FileResults);
    Assert.Null(file.Result.RewrittenSource);
    Assert.DoesNotContain(
      outcome.Result.SeedMarks,
      mark => string.Equals(mark.RuleId, "mark.unreferenced-method", StringComparison.Ordinal));
    Assert.Empty(outcome.Result.Edits);
    Assert.Equal(0, outcome.Result.Stats?.DeletedMethodCount);
  }

  [Fact]
  public void Analyze_MethodGlobalRules_WhenCompilationHasErrors_ProduceNoMethodMarks()
  {
    var useCase = new DirectoryAnalysisUseCase(
      RuleRegistry.CreateDefaultRules(
        enableUnreachableMethodDeletion: true,
        enableUnreferencedMethodDeletion: true));
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "Program.cs",
        """
        public static class Program
        {
          public static void Main()
          {
            Live();
          }

          private static void Live()
          {
            MissingCall();
          }

          private static void Dead() { }
        }
        """)
    };

    var outcome = useCase.Analyze(
      sources,
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
      AnalysisRuntime.CreateDefault());

    Assert.DoesNotContain(
      outcome.Result.SeedMarks,
      mark => mark.RuleId is "mark.unreachable-method" or "mark.unreferenced-method");
    Assert.Empty(outcome.Result.Edits);
  }

  [Fact]
  public void Analyze_MethodGlobalRules_PreservesEntryClosureAndMarksOnlyIsolatedPrivateCycle()
  {
    var useCase = new DirectoryAnalysisUseCase(
      RuleRegistry.CreateDefaultRules(
        enableUnreachableMethodDeletion: true,
        enableUnreferencedMethodDeletion: true));
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "Program.cs",
        """
        public static class Program
        {
          public static void Main()
          {
            Entry();
          }

          public static void Entry()
          {
            A();
          }

          private static void A()
          {
            B();
          }

          private static void B() { }

          private static void CycleA()
          {
            CycleB();
          }

          private static void CycleB()
          {
            CycleA();
          }
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

    var methodGlobalMarks = outcome.Result.SeedMarks
      .Where(mark => mark.RuleId is "mark.unreachable-method" or "mark.unreferenced-method")
      .ToArray();
    Assert.DoesNotContain(methodGlobalMarks, mark => HasMethodName(mark, "A"));
    Assert.DoesNotContain(methodGlobalMarks, mark => HasMethodName(mark, "B"));
    Assert.Contains(methodGlobalMarks, mark => HasMethodName(mark, "CycleA"));
    Assert.Contains(methodGlobalMarks, mark => HasMethodName(mark, "CycleB"));

    var rewrittenSource = Assert.IsType<string>(Assert.Single(outcome.FileResults).Result.RewrittenSource);
    Assert.Contains("A();", rewrittenSource, StringComparison.Ordinal);
    Assert.Contains("B();", rewrittenSource, StringComparison.Ordinal);
    var rewrittenCompilation = RoslynCompilationFactory.CreateCompilation(
      CSharpSyntaxTree.ParseText(rewrittenSource, path: "Program.cs"));
    Assert.DoesNotContain(
      rewrittenCompilation.GetDiagnostics(),
      diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void Analyze_DeleteUnreferencedMethods_NestedExecutableReference_RetainsPrivateMethods()
  {
    var useCase = new DirectoryAnalysisUseCase(
      RuleRegistry.CreateDefaultRules(enableUnreferencedMethodDeletion: true));
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "LocalFunction.cs",
        """
        public static class Sample
        {
          private static void Container()
          {
            void Callback() => Target();
            Callback();
            System.Action lambda = () => LambdaTarget();
            lambda();
          }

          private static void Target() { }
          private static void LambdaTarget() { }
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

    Assert.DoesNotContain(
      outcome.Result.SeedMarks,
      mark => mark.RuleId == "mark.unreferenced-method" &&
        (HasMethodName(mark, "Target") || HasMethodName(mark, "LambdaTarget")));
  }

  [Fact]
  public void Analyze_MethodGlobalRules_AreEquivalentAcrossDirectoryParallelism()
  {
    var sources = new[]
    {
      new DirectorySourceFile(
        0,
        "Program.cs",
        """
        public static partial class Program
        {
          public static void Main() => Entry();
          public static void Entry() => LiveA();
          private static void LiveA() => LiveB();
        }
        """),
      new DirectorySourceFile(
        1,
        "Methods.cs",
        """
        public static partial class Program
        {
          private static void LiveB() { }
          private static void DeadA() => DeadB();
          private static void DeadB() => DeadA();
        }
        """)
    };
    var expected = AnalyzeMethodGlobalAtDegree(sources, 1);

    AssertMethodGlobalSnapshotsEqual(expected, AnalyzeMethodGlobalAtDegree(sources, 2));
    AssertMethodGlobalSnapshotsEqual(expected, AnalyzeMethodGlobalAtDegree(sources, 16));
  }

  private static bool HasMethodName(MarkRecord mark, string name)
  {
    return mark.SyntaxNode is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method &&
      string.Equals(method.Identifier.ValueText, name, StringComparison.Ordinal);
  }

  private static MethodGlobalSnapshot AnalyzeMethodGlobalAtDegree(
    IReadOnlyList<DirectorySourceFile> sources,
    int degreeOfParallelism)
  {
    var useCase = new DirectoryAnalysisUseCase(
      RuleRegistry.CreateDefaultRules(
        enableUnreachableMethodDeletion: true,
        enableUnreferencedMethodDeletion: true));
    var runtime = AnalysisRuntimeFactory.Create(
      new RoslynPrototypeExecutionOptions(
        degreeOfParallelism,
        EnableDirectoryParallelism: true,
        CpgMaxDegreeOfParallelism: 1));
    var outcome = useCase.Analyze(
      sources,
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["delete-unreferenced-methods"] = "true"
      },
      runtime);
    var result = outcome.Result;
    return new MethodGlobalSnapshot(
      result.SeedMarks
        .Where(mark => mark.RuleId is "mark.unreachable-method" or "mark.unreferenced-method")
        .Select(mark => $"{mark.RuleId}|{mark.SyntaxNode.SyntaxTree.FilePath}|{mark.SyntaxNode.Span}|{mark.SyntaxNode}")
        .ToArray(),
      result.Decisions
        .Where(decision => decision.RuleId is "propose.unreachable-method" or "propose.unreferenced-method")
        .Select(decision => $"{decision.RuleId}|{decision.Action}|{decision.FinalNode.SyntaxTree.FilePath}|{decision.FinalNode.Span}")
        .ToArray(),
      result.RewritePlans!
        .Select(plan => $"{plan.FilePath}|{string.Join(',', plan.Operations.Select(operation => operation.ToString()))}")
        .ToArray(),
      outcome.FileResults.Select(file => file.Result.RewrittenSource ?? string.Empty).ToArray(),
      outcome.FileResults.Select(file => file.Result.Diff.ToString()).ToArray());
  }

  private static void AssertMethodGlobalSnapshotsEqual(
    MethodGlobalSnapshot expected,
    MethodGlobalSnapshot actual)
  {
    Assert.Equal(expected.SeedMarks, actual.SeedMarks);
    Assert.Equal(expected.Decisions, actual.Decisions);
    Assert.Equal(expected.RewritePlans, actual.RewritePlans);
    Assert.Equal(expected.RewrittenSources, actual.RewrittenSources);
    Assert.Equal(expected.Diffs, actual.Diffs);
  }

  private sealed record MethodGlobalSnapshot(
    IReadOnlyList<string> SeedMarks,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> RewritePlans,
    IReadOnlyList<string> RewrittenSources,
    IReadOnlyList<string> Diffs);
}
