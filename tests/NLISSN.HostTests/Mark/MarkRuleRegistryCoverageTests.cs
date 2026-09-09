using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder;
using NLISSN.Application;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
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

    var marks = rule.Mark(context.CreateMarkContext(), root).ToArray();

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

    var marks = rule.Mark(context.CreateMarkContext(), root).ToArray();

    Assert.Empty(marks);
  }

  [Theory]
  [MemberData(nameof(NonTargetScenarios))]
  public void Mark_NonTargetRule_EmitsOnlyItsExpectedSyntaxNode(MarkRuleScenario scenario)
  {
    var (context, root) = CreateRuleContext(scenario.Source, scenario.Options);
    var rule = GetMarker(scenario.RuleId);

    var marks = rule.Mark(context.CreateMarkContext(), root).ToArray();

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

    var marks = rule.Mark(context.CreateMarkContext(), root).ToArray();

    Assert.Empty(marks);
  }

  [Fact]
  public void Mark_AtomicIdentifierRule_MultipleMatchesAreDistinctAndSourceOrdered()
  {
    const string source = "public sealed class Sample { public int Run(int target) { return target + target; } }";
    var (context, root) = CreateRuleContext(source, "target");
    var rule = GetMarker("mark.target.identifier-name");

    var marks = rule.Mark(context.CreateMarkContext(), root).ToArray();

    Assert.Equal(2, marks.Length);
    AssertMarksAreUniqueAndInStableOrder(marks);
  }

  [Fact]
  public void Mark_AtomicIdentifierRule_IgnoresTargetTextInStringsAndComments()
  {
    const string source = "public sealed class Sample { public int Run() { var label = \"target\"; // target\n return label.Length; } }";
    var (context, root) = CreateRuleContext(source, "target");
    var rule = GetMarker("mark.target.identifier-name");

    var marks = rule.Mark(context.CreateMarkContext(), root).ToArray();

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
    var result = new  ApplicationService(RulePipelineTestFactory.Create()).Analyze(
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
    var registeredRuleIds = RulePipelineTestFactory.Create()
      .Markers
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
      "mark.target.identifier-name",
      "public sealed class Sample { public int Run(int target) { return target; } }",
      "target",
      SyntaxKind.IdentifierName,
      "target");
    yield return Scenario(
      "mark.target.this-expression",
      "public sealed class Sample { public Sample Run() { return this; } }",
      "this",
      SyntaxKind.ThisExpression,
      "this");
    yield return Scenario(
      "mark.target.base-expression",
      "public class Base { } public sealed class Sample : Base { public Base Run() { return base; } }",
      "base",
      SyntaxKind.BaseExpression,
      "base");
    yield return Scenario(
      "mark.target.variable-declarator",
      "public sealed class Sample { public int Run() { int target = 1; return target; } }",
      "target",
      SyntaxKind.VariableDeclarator,
      "target = 1");
    yield return Scenario(
      "mark.target.numeric-literal",
      "public sealed class Sample { public int Run() { return 42; } }",
      "42",
      SyntaxKind.NumericLiteralExpression,
      "42");
    yield return Scenario(
      "mark.target.string-literal",
      "public sealed class Sample { public string Run() { return \"target\"; } }",
      "target",
      SyntaxKind.StringLiteralExpression,
      "\"target\"");
    yield return Scenario(
      "mark.target.true-literal",
      "public sealed class Sample { public bool Run() { return true; } }",
      "true",
      SyntaxKind.TrueLiteralExpression,
      "true");
    yield return Scenario(
      "mark.target.false-literal",
      "public sealed class Sample { public bool Run() { return false; } }",
      "false",
      SyntaxKind.FalseLiteralExpression,
      "false");
    yield return Scenario(
      "mark.target.null-literal",
      "public sealed class Sample { public object? Run() { return null; } }",
      "null",
      SyntaxKind.NullLiteralExpression,
      "null");
    yield return Scenario(
      "mark.target.member-access",
      "public sealed class Target { public int Value; } public sealed class Sample { public int Run(Target target) { return target.Value; } }",
      "Value",
      SyntaxKind.SimpleMemberAccessExpression,
      "target.Value");
    yield return Scenario(
      "mark.target.member-binding",
      "using System; public sealed class Sample { public int? Run(Func<int>? handler) { return handler?.Invoke(); } }",
      "Invoke",
      SyntaxKind.MemberBindingExpression,
      ".Invoke");
    yield return Scenario(
      "mark.target.invocation",
      "public sealed class Sample { public int Run() { return target(); } private int target() { return 1; } }",
      "target",
      SyntaxKind.InvocationExpression,
      "target()");
    yield return Scenario(
      "mark.target.object-creation",
      "public sealed class Target { } public sealed class Sample { public Target Run() { return new Target(); } }",
      "Target",
      SyntaxKind.ObjectCreationExpression,
      "new Target()");
    yield return Scenario(
      "mark.target.implicit-object-creation",
      "public sealed class Target { } public sealed class Sample { public void Run() { Target target = new(); } }",
      "Target",
      SyntaxKind.ImplicitObjectCreationExpression,
      "new()");
    yield return Scenario(
      "mark.target.element-access",
      "public sealed class Sample { public int Run(int[] target) { return target[0]; } }",
      "target",
      SyntaxKind.ElementAccessExpression,
      "target[0]");
    yield return Scenario(
      "mark.target.conditional-access",
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
      "mark.type.declaration",
      "public sealed class Target { } public sealed class Consumer { }",
      Options(("delete-class", "Target")),
      SyntaxKind.ClassDeclaration,
      "public sealed class Target { }");
    yield return Scenario(
      "mark.type.expression",
      "public sealed class Target { } public sealed class Consumer { public Target Create() { return new Target(); } }",
      Options(("delete-class", "Target")),
      SyntaxKind.ObjectCreationExpression,
      "new Target()");
    yield return Scenario(
      "mark.type.type-syntax",
      "public sealed class Target { } public sealed class Consumer { private Target _target; }",
      Options(("delete-class", "Target")),
      SyntaxKind.IdentifierName,
      "Target");
  }

  public static IEnumerable<object[]> NonTargetNegativeScenarios()
  {
    yield return NegativeScenario(
      "mark.type.declaration",
      "public sealed class Target { }",
      Options(("delete-class", "Other")));
    yield return NegativeScenario(
      "mark.type.expression",
      "public sealed class Target { } public sealed class Consumer { public Target Create() { return new Target(); } }",
      Options(("delete-class", "Other")));
    yield return NegativeScenario(
      "mark.type.type-syntax",
      "public sealed class Target { } public sealed class Consumer { private Target _target; }",
      Options(("delete-class", "Other")));
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
    var application = new ApplicationService(CreateRules(ruleId));
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
    return CreateRules(ruleId).Markers.Single(rule => rule.RuleId == ruleId);
  }

  private static RulePipeline CreateRules(string ruleId)
  {
    return RulePipelineTestFactory.Create();
  }

  private static (AnalysisSession Context, SyntaxNode Root) CreateRuleContext(string source, string targetName)
  {
    return CreateRuleContext(source, Options(("target-name", targetName)));
  }

  private static (AnalysisSession Context, SyntaxNode Root) CreateRuleContext(string source, IReadOnlyDictionary<string, string> options)
  {
    const string filePath = "MarkRuleRegistryCoverage.cs";
    var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var root = tree.GetRoot();
    var compilation = RoslynCompilationFactory.CreateCompilation(tree);
    var semanticModel = compilation.GetSemanticModel(tree);
    var graph = new NLCPGBuilder().BuildFromSource(source, filePath);
    return (new AnalysisSession(
      new CpgAnalysisContext(graph, semanticModel, root),
      AnalysisLegacyOptionsTestExtensions.CreateSettings(options)), root);
  }
}
