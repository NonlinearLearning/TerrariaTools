using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder;
using NLISSN.Application;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MarkRuleRegistryCoverageTests
{
  [Theory]
  [MemberData(nameof(AtomicScenarios))]
  public void Mark_AtomicRule_EmitsOnlyItsExpectedAtomicNode(MarkRuleScenario scenario)
  {
    var (context, root) = CreateRuleContext(scenario.Source, scenario.Options);
    var rule = GetMarker(scenario.RuleId);
    var expectedSyntax = root.DescendantNodes()
      .Single(node => node.IsKind(scenario.ExpectedKind) && node.ToString() == scenario.ExpectedText);

    Assert.True(
      context.TryResolvePrimaryGraphNode(expectedSyntax, out var primaryGraphNode),
      $"Expected graph binding for {scenario.ExpectedKind} '{scenario.ExpectedText}' at {expectedSyntax.Span}.");
    Assert.NotNull(primaryGraphNode);
    if (expectedSyntax is ThisExpressionSyntax or BaseExpressionSyntax)
    {
      Assert.IsAssignableFrom<IInstanceReferenceOperation>(context.GetCachedOperation(expectedSyntax));
    }

    var marks = rule.Mark(context.CreateMarkRuleContext(), root).ToArray();

    var mark = Assert.Single(marks);
    Assert.Equal(scenario.RuleId, mark.RuleId);
    Assert.Equal(scenario.ExpectedKind, (SyntaxKind)mark.SyntaxNode.RawKind);
    Assert.Equal(expectedSyntax.SpanStart, mark.SyntaxNode.SpanStart);
    Assert.Equal(expectedSyntax.Span.Length, mark.SyntaxNode.Span.Length);
    Assert.Equal(scenario.ExpectedText, mark.SyntaxNode.ToString());
    Assert.Contains(scenario.ExpectedKind, rule.AllowedMarkNodeKinds);
    AssertMarksAreUniqueAndInStableOrder(marks);
  }

  [Theory]
  [MemberData(nameof(AtomicNegativeScenarios))]
  public void Mark_AtomicRule_WhenTargetDoesNotExist_ProducesNoMark(MarkRuleScenario scenario)
  {
    var options = Options(("target-name", "not_a_target"));
    var (context, root) = CreateRuleContext(scenario.Source, options);
    var rule = GetMarker(scenario.RuleId);

    var marks = rule.Mark(context.CreateMarkRuleContext(), root).ToArray();

    Assert.Empty(marks);
  }

  [Theory]
  [MemberData(nameof(NonTargetScenarios))]
  public void Mark_NonTargetRule_EmitsOnlyItsExpectedSyntaxNode(MarkRuleScenario scenario)
  {
    var (context, root) = CreateRuleContext(scenario.Source, scenario.Options);
    var rule = GetMarker(scenario.RuleId);

    var marks = rule.Mark(context.CreateMarkRuleContext(), root).ToArray();

    var mark = Assert.Single(marks);
    var expectedSyntax = root.DescendantNodes()
      .Single(node => node.IsKind(scenario.ExpectedKind) && node.ToString() == scenario.ExpectedText);
    Assert.Equal(scenario.RuleId, mark.RuleId);
    Assert.Equal(scenario.ExpectedKind, (SyntaxKind)mark.SyntaxNode.RawKind);
    Assert.Equal(expectedSyntax.SpanStart, mark.SyntaxNode.SpanStart);
    Assert.Equal(expectedSyntax.Span.Length, mark.SyntaxNode.Span.Length);
    Assert.Equal(scenario.ExpectedText, mark.SyntaxNode.ToString());
    Assert.Contains(scenario.ExpectedKind, rule.AllowedMarkNodeKinds);
    AssertMarksAreUniqueAndInStableOrder(marks);
  }

  [Theory]
  [MemberData(nameof(NonTargetNegativeScenarios))]
  public void Mark_NonTargetRule_WhenSemanticPredicateDoesNotHold_ProducesNoMark(MarkRuleNegativeScenario scenario)
  {
    var (context, root) = CreateRuleContext(scenario.Source, scenario.Options);
    var rule = GetMarker(scenario.RuleId);

    var marks = rule.Mark(context.CreateMarkRuleContext(), root).ToArray();

    Assert.Empty(marks);
  }

  [Fact]
  public void Mark_AtomicIdentifierRule_MultipleMatchesAreDistinctAndSourceOrdered()
  {
    const string source = "public sealed class Sample { public int Run(int target) { return target + target; } }";
    var (context, root) = CreateRuleContext(source, "target");
    var rule = GetMarker("DEL-SOBJ-MARK-ID-001");

    var marks = rule.Mark(context.CreateMarkRuleContext(), root).ToArray();

    Assert.Equal(2, marks.Length);
    AssertMarksAreUniqueAndInStableOrder(marks);
  }

  [Fact]
  public void Mark_AtomicIdentifierRule_IgnoresTargetTextInStringsAndComments()
  {
    const string source = "public sealed class Sample { public int Run() { var label = \"target\"; // target\n return label.Length; } }";
    var (context, root) = CreateRuleContext(source, "target");
    var rule = GetMarker("DEL-SOBJ-MARK-ID-001");

    var marks = rule.Mark(context.CreateMarkRuleContext(), root).ToArray();

    Assert.Empty(marks);
  }

  [Theory]
  [MemberData(nameof(AtomicScenarios))]
  public void Pipeline_AtomicScenario_DirectAndPlanReplaysAreEquivalentAndCompilable(MarkRuleScenario scenario)
  {
    AssertFullPipelineOutput(
      scenario.Source,
      scenario.Options,
      scenario.RuleId,
      scenario.ExpectedKind,
      scenario.ExpectedText);
  }

  [Theory]
  [MemberData(nameof(NonTargetScenarios))]
  public void Pipeline_NonTargetScenario_DirectAndPlanReplaysAreEquivalentAndCompilable(MarkRuleScenario scenario)
  {
    AssertFullPipelineOutput(
      scenario.Source,
      scenario.Options,
      scenario.RuleId,
      scenario.ExpectedKind,
      scenario.ExpectedText);
  }

  [Fact]
  public void Pipeline_AtomicReturnExpressions_ReplacesWithDeclaredReturnTypeValues()
  {
    const string source = """
      public sealed class Target
      {
        public Target() { }
      }

      public abstract class AbstractTarget { }

      public sealed class NoDefaultTarget
      {
        public NoDefaultTarget(int value) { }
      }

      public sealed class Sample
      {
        public int Number(int target)
        {
          return target;
        }

        public Target Object(Target target)
        {
          return target;
        }

        public AbstractTarget Abstract(AbstractTarget target)
        {
          return target;
        }

        public NoDefaultTarget NoDefault(NoDefaultTarget target)
        {
          return target;
        }
      }
      """;
    var result = new  ApplicationService(RuleRegistry.CreateDefaultRules()).Analyze(
      source,
      "ReturnReplacement.cs",
      Options(("target-name", "target")));
    var rewrittenSource = Assert.IsType<string>(result.RewrittenSource);

    Assert.True(
      rewrittenSource.Contains("return default(int);", StringComparison.Ordinal),
      rewrittenSource);
    Assert.True(
      rewrittenSource.Contains("return new Target();", StringComparison.Ordinal),
      rewrittenSource);
    Assert.True(
      rewrittenSource.Contains("return default(AbstractTarget);", StringComparison.Ordinal),
      rewrittenSource);
    Assert.True(
      rewrittenSource.Contains("return default(NoDefaultTarget);", StringComparison.Ordinal),
      rewrittenSource);
    Assert.Empty(GetCompilationErrors(rewrittenSource, "ReturnReplacement.cs"));
  }

  [Fact]
  public void CreateDefaultRules_Markers_RequireAnExplicitScenarioForEveryRule()
  {
    var registeredRuleIds = RuleRegistry.CreateDefaultRules().Markers
      .Select(rule => rule.RuleId)
      .ToHashSet(StringComparer.Ordinal);
    var uncoveredRuleIds = registeredRuleIds
      .Except(CoveredRuleIds, StringComparer.Ordinal)
      .OrderBy(ruleId => ruleId, StringComparer.Ordinal)
      .ToArray();
    var staleScenarioRuleIds = CoveredRuleIds
      .Except(registeredRuleIds, StringComparer.Ordinal)
      .OrderBy(ruleId => ruleId, StringComparer.Ordinal)
      .ToArray();

    Assert.True(
      uncoveredRuleIds.Length == 0 && staleScenarioRuleIds.Length == 0,
      $"Uncovered Mark rules: {string.Join(", ", uncoveredRuleIds)}; " +
      $"stale Mark scenarios: {string.Join(", ", staleScenarioRuleIds)}.");
  }

  private static readonly IReadOnlySet<string> CoveredRuleIds =
    AllScenarios()
      .Select(scenario => scenario.RuleId)
      .ToHashSet(StringComparer.Ordinal);

  private static IEnumerable<MarkRuleScenario> AllScenarios()
  {
    return AtomicScenarios()
      .Concat(NonTargetScenarios())
      .Select(values => (MarkRuleScenario)values[0]);
  }

  public static IEnumerable<object[]> AtomicScenarios()
  {
    yield return Scenario(
      "DEL-SOBJ-MARK-ID-001",
      "public sealed class Sample { public int Run(int target) { return target; } }",
      "target",
      SyntaxKind.IdentifierName,
      "target");
    yield return Scenario(
      "DEL-SOBJ-MARK-THIS-001",
      "public sealed class Sample { public Sample Run() { return this; } }",
      "this",
      SyntaxKind.ThisExpression,
      "this");
    yield return Scenario(
      "DEL-SOBJ-MARK-BASE-001",
      "public class Base { } public sealed class Sample : Base { public Base Run() { return base; } }",
      "base",
      SyntaxKind.BaseExpression,
      "base");
    yield return Scenario(
      "DEL-SOBJ-MARK-DECL-001",
      "public sealed class Sample { public int Run() { int target = 1; return target; } }",
      "target",
      SyntaxKind.VariableDeclarator,
      "target = 1");
    yield return Scenario(
      "DEL-SOBJ-MARK-LIT-NUM-001",
      "public sealed class Sample { public int Run() { return 42; } }",
      "42",
      SyntaxKind.NumericLiteralExpression,
      "42");
    yield return Scenario(
      "DEL-SOBJ-MARK-LIT-STR-001",
      "public sealed class Sample { public string Run() { return \"target\"; } }",
      "target",
      SyntaxKind.StringLiteralExpression,
      "\"target\"");
    yield return Scenario(
      "DEL-SOBJ-MARK-LIT-TRUE-001",
      "public sealed class Sample { public bool Run() { return true; } }",
      "true",
      SyntaxKind.TrueLiteralExpression,
      "true");
    yield return Scenario(
      "DEL-SOBJ-MARK-LIT-FALSE-001",
      "public sealed class Sample { public bool Run() { return false; } }",
      "false",
      SyntaxKind.FalseLiteralExpression,
      "false");
    yield return Scenario(
      "DEL-SOBJ-MARK-LIT-NULL-001",
      "public sealed class Sample { public object? Run() { return null; } }",
      "null",
      SyntaxKind.NullLiteralExpression,
      "null");
    yield return Scenario(
      "DEL-SOBJ-MARK-MEMBER-001",
      "public sealed class Target { public int Value; } public sealed class Sample { public int Run(Target target) { return target.Value; } }",
      "Value",
      SyntaxKind.SimpleMemberAccessExpression,
      "target.Value");
    yield return Scenario(
      "DEL-SOBJ-MARK-BINDING-001",
      "using System; public sealed class Sample { public int? Run(Func<int>? handler) { return handler?.Invoke(); } }",
      "Invoke",
      SyntaxKind.MemberBindingExpression,
      ".Invoke");
    yield return Scenario(
      "DEL-SOBJ-MARK-INVOKE-001",
      "public sealed class Sample { public int Run() { return target(); } private int target() { return 1; } }",
      "target",
      SyntaxKind.InvocationExpression,
      "target()");
    yield return Scenario(
      "DEL-SOBJ-MARK-NEW-001",
      "public sealed class Target { } public sealed class Sample { public Target Run() { return new Target(); } }",
      "Target",
      SyntaxKind.ObjectCreationExpression,
      "new Target()");
    yield return Scenario(
      "DEL-SOBJ-MARK-IMPLICIT-NEW-001",
      "public sealed class Target { } public sealed class Sample { public void Run() { Target target = new(); } }",
      "Target",
      SyntaxKind.ImplicitObjectCreationExpression,
      "new()");
    yield return Scenario(
      "DEL-SOBJ-MARK-ELEMENT-001",
      "public sealed class Sample { public int Run(int[] target) { return target[0]; } }",
      "target",
      SyntaxKind.ElementAccessExpression,
      "target[0]");
    yield return Scenario(
      "DEL-SOBJ-MARK-CONDITIONAL-001",
      "public sealed class Target { public int Value; } public sealed class Sample { public int? Run(Target? target) { return target?.Value; } }",
      "target",
      SyntaxKind.ConditionalAccessExpression,
      "target?.Value");
  }

  public static IEnumerable<object[]> AtomicNegativeScenarios()
  {
    return AtomicScenarios()
      .Select(scenario => new[] { scenario[0] });
  }

  public static IEnumerable<object[]> NonTargetScenarios()
  {
    yield return Scenario(
      "CLR-UNUSED-IFACE-IMPL-MARK-001",
      "public interface ITarget { void Remove(); } public sealed class Sample : ITarget { public void Remove() { } }",
      Options(("clear-unused-interface-implementations", "true")),
      SyntaxKind.MethodDeclaration,
      "public void Remove() { }");
    yield return Scenario(
      "DEL-CLASS-MARK-DECL-001",
      "public sealed class Target { } public sealed class Consumer { }",
      Options(("delete-class", "Target")),
      SyntaxKind.ClassDeclaration,
      "public sealed class Target { }");
    yield return Scenario(
      "DEL-CLASS-MARK-EXPR-001",
      "public sealed class Target { } public sealed class Consumer { public Target Create() { return new Target(); } }",
      Options(("delete-class", "Target")),
      SyntaxKind.ObjectCreationExpression,
      "new Target()");
    yield return Scenario(
      "DEL-CLASS-MARK-TYPE-001",
      "public sealed class Target { } public sealed class Consumer { private Target _target; }",
      Options(("delete-class", "Target")),
      SyntaxKind.IdentifierName,
      "Target");
    yield return Scenario(
      "DEL-DEAD-001",
      "public static class Program { public static void Main() { } private static void Dead() { } }",
      Options(),
      SyntaxKind.MethodDeclaration,
      "private static void Dead() { }");
    yield return Scenario(
      "DEL-UNREF-METHOD-MARK-001",
      "public sealed class Sample { public void Run() { } private void Remove() { } }",
      Options(("delete-unreferenced-methods", "true")),
      SyntaxKind.MethodDeclaration,
      "private void Remove() { }");
    yield return Scenario(
      "mark.privatize-internal-only-public-method",
      "public sealed class Sample { public void Target() { } public void Run() { Target(); } }",
      Options(("privatize-internal-only-public-methods", "true")),
      SyntaxKind.MethodDeclaration,
      "public void Target() { }");
  }

  public static IEnumerable<object[]> NonTargetNegativeScenarios()
  {
    yield return NegativeScenario(
      "CLR-UNUSED-IFACE-IMPL-MARK-001",
      "public interface ITarget { void Remove(); } public sealed class Sample : ITarget { public void Remove() { } } public static class Use { public static void Run(ITarget target) { target.Remove(); } }",
      Options(("clear-unused-interface-implementations", "true")));
    yield return NegativeScenario(
      "DEL-CLASS-MARK-DECL-001",
      "public sealed class Target { }",
      Options(("delete-class", "Other")));
    yield return NegativeScenario(
      "DEL-CLASS-MARK-EXPR-001",
      "public sealed class Target { } public sealed class Consumer { public Target Create() { return new Target(); } }",
      Options(("delete-class", "Other")));
    yield return NegativeScenario(
      "DEL-CLASS-MARK-TYPE-001",
      "public sealed class Target { } public sealed class Consumer { private Target _target; }",
      Options(("delete-class", "Other")));
    yield return NegativeScenario(
      "DEL-DEAD-001",
      "public static class Program { public static void Main() { Live(); } private static void Live() { } }",
      Options());
    yield return NegativeScenario(
      "DEL-UNREF-METHOD-MARK-001",
      "public sealed class Sample { public void Run() { Remove(); } private void Remove() { } }",
      Options(("delete-unreferenced-methods", "true")));
    yield return NegativeScenario(
      "mark.privatize-internal-only-public-method",
      "public sealed class Sample { public void Target() { } } public sealed class Consumer { public void Run(Sample sample) { sample.Target(); } }",
      Options(("privatize-internal-only-public-methods", "true")));
  }

  private static object[] Scenario(string ruleId, string source, string targetName, SyntaxKind expectedKind, string expectedText)
  {
    return new object[]
    {
      new MarkRuleScenario(
        ruleId,
        source,
        Options(("target-name", targetName)),
        expectedKind,
        expectedText),
    };
  }

  private static object[] Scenario(string ruleId, string source, IReadOnlyDictionary<string, string> options, SyntaxKind expectedKind, string expectedText)
  {
    return new object[] { new MarkRuleScenario(ruleId, source, options, expectedKind, expectedText) };
  }

  private static object[] NegativeScenario(string ruleId, string source, IReadOnlyDictionary<string, string> options)
  {
    return new object[] { new MarkRuleNegativeScenario(ruleId, source, options) };
  }

  public sealed record MarkRuleScenario(
    string RuleId,
    string Source,
    IReadOnlyDictionary<string, string> Options,
    SyntaxKind ExpectedKind,
    string ExpectedText);

  public sealed record MarkRuleNegativeScenario(
    string RuleId,
    string Source,
    IReadOnlyDictionary<string, string> Options);

  private static IReadOnlyDictionary<string, string> Options(params (string Key, string Value)[] values)
  {
    return values.ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);
  }

  private static void AssertMarksAreUniqueAndInStableOrder(IReadOnlyList<MarkRecord> marks)
  {
    Assert.Equal(
      marks.Count,
      marks.Select(mark => (mark.SyntaxNode.Span, mark.SyntaxNode.RawKind)).Distinct().Count());
    Assert.Equal(
      marks.OrderBy(mark => mark.SyntaxNode.SpanStart)
        .ThenByDescending(mark => mark.SyntaxNode.Span.Length)
        .Select(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind)),
      marks.Select(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind)));
  }

  private static void AssertFullPipelineOutput(string source, IReadOnlyDictionary<string, string> options, string ruleId, SyntaxKind expectedKind, string expectedText)
  {
    const string filePath = "Scenario.cs";
    var sourceWithUnrelatedDeclaration = source + "\npublic sealed class Unrelated { }";
    var application = new  ApplicationService(RuleRegistry.CreateDefaultRules());
    var result = application.Analyze(sourceWithUnrelatedDeclaration, filePath, options);
    var directSource = result.RewrittenSource ?? sourceWithUnrelatedDeclaration;
    var rewritePlans = result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>();
    var ruleMark = Assert.Single(result.SeedMarks, mark => mark.RuleId == ruleId);
    Assert.Equal(expectedKind, (SyntaxKind)ruleMark.SyntaxNode.RawKind);
    Assert.Equal(expectedText, ruleMark.SyntaxNode.ToString());
    Assert.Contains(result.Edits, edit => edit.OriginalText.Contains(expectedText, StringComparison.Ordinal));
    Assert.Contains("class Unrelated", directSource, StringComparison.Ordinal);

    if (rewritePlans.Count == 0)
    {
      Assert.Empty(result.Edits);
      Assert.Equal(sourceWithUnrelatedDeclaration, directSource);
    }
    else
    {
      var plan = Assert.Single(rewritePlans);
      var rewriter = new PrototypeRewriter();
      var inMemory = rewriter.ExecutePlan(
        sourceWithUnrelatedDeclaration,
        filePath,
        new PrototypeRewritePlan(plan.Operations, result.Edits));
      var temporaryRoot = Path.Combine(Path.GetTempPath(), $"mark-rule-plan-{Guid.NewGuid():N}");
      var inputRoot = Path.Combine(temporaryRoot, "input");
      var artifactRoot = Path.Combine(temporaryRoot, "artifact");
      Directory.CreateDirectory(inputRoot);

      try
      {
        var sourcePath = Path.Combine(inputRoot, filePath);
        File.WriteAllText(sourcePath, sourceWithUnrelatedDeclaration);
        var artifactService = new RewritePlanArtifactService();
        artifactService.Write(
          artifactRoot,
          inputRoot,
          sourceFileCount: 1,
          new[]
          {
            new RewritePlanFile(
              filePath,
              RewritePlanArtifactService.ComputeSha256(File.ReadAllBytes(sourcePath)),
              plan.Operations),
          });
        var (_, persistedPlans) = artifactService.ReadAndValidate(artifactRoot, inputRoot);
        var persisted = rewriter.ExecutePlan(
          sourceWithUnrelatedDeclaration,
          filePath,
          Assert.Single(persistedPlans));

        Assert.Equal(directSource, inMemory.RewrittenSource);
        Assert.Equal(directSource, persisted.RewrittenSource);
        Assert.Equal(result.Edits, inMemory.Edits);
        Assert.Equal(result.Edits, persisted.Edits);
        Assert.Equal(result.Diff.ToString(), inMemory.Diff.ToString());
        Assert.Equal(result.Diff.ToString(), persisted.Diff.ToString());
      }
      finally
      {
        Directory.Delete(temporaryRoot, recursive: true);
      }
    }

    Assert.Empty(GetCompilationErrors(directSource, filePath));
  }

  private static IReadOnlyList<Diagnostic> GetCompilationErrors(string source, string filePath)
  {
    var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var compilation = CSharpCompilation.Create(
      "MarkRuleRegistryCoverageOutput",
      new[] { tree },
      new[]
      {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
      },
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    return compilation.GetDiagnostics()
      .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
      .ToArray();
  }

  private static RuleDefinitionMark GetMarker(string ruleId)
  {
    return RuleRegistry.CreateDefaultRules().Markers.Single(rule => rule.RuleId == ruleId);
  }

  private static (RuleContext Context, SyntaxNode Root) CreateRuleContext(string source, string targetName)
  {
    return CreateRuleContext(source, Options(("target-name", targetName)));
  }

  private static (RuleContext Context, SyntaxNode Root) CreateRuleContext(string source, IReadOnlyDictionary<string, string> options)
  {
    const string filePath = "MarkRuleRegistryCoverage.cs";
    var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var root = tree.GetRoot();
    var compilation = CSharpCompilation.Create(
      "MarkRuleRegistryCoverageTests",
      new[] { tree },
      new[]
      {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
      });
    var semanticModel = compilation.GetSemanticModel(tree);
    var graph = new NLCPGBuilder().BuildFromSource(source, filePath);
    return (new RuleContext(new CpgAnalysisContext(graph, semanticModel, root), options), root);
  }
}
