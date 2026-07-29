using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Rewrite;

public sealed class DiffModelTests
{
  [Fact]
  public void PrototypeRewriter_BuildPlanAndExecutePlan_MatchesDirectRewrite()
  {
    const string source = RewriteSources.SimpleValueReturnSource;

    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var root = tree.GetRoot();
    var compilation = CSharpCompilation.Create("sample", new[] { tree });
    var semanticModel = compilation.GetSemanticModel(tree);
    var identifier = root.DescendantNodes().OfType<IdentifierNameSyntax>()
      .Single(node => node.Identifier.ValueText == "value");
    var decisions = new[]
    {
      new RuleDecision(
        identifier,
        identifier,
        DecisionActionKind.Delete,
        "Replace identifier with its default value.")
    };
    var rewriter = new PrototypeRewriter();

    var direct = rewriter.Rewrite(root, semanticModel, decisions);
    var plan = rewriter.BuildPlan(root, semanticModel, decisions);
    var replayed = rewriter.ExecutePlan(source, "sample.cs", plan);
    var persistedPlan = new RewritePlanFile("sample.cs", "unused", plan.Operations);
    var replayedPersistedPlan = rewriter.ExecutePlan(source, "sample.cs", persistedPlan);

    Assert.Equal(direct.RewrittenSource, replayed.RewrittenSource);
    Assert.Equal(direct.Edits, replayed.Edits);
    Assert.Equal(direct.Diff.ToString(), replayed.Diff.ToString());
    Assert.Equal(direct.RewrittenSource, replayedPersistedPlan.RewrittenSource);
    Assert.Equal(direct.Edits, replayedPersistedPlan.Edits);
    Assert.Equal(direct.Diff.ToString(), replayedPersistedPlan.Diff.ToString());
  }

  [Fact]
  public void PrototypeRewriter_MultipleExpressionDeletes_DirectAndPlanReplaysRemainEquivalentAndCompilable()
  {
    const string source = """
      namespace Demo;
      public sealed class Sample
      {
        public int Run(int left, int right) => left + right;
      }
      """;
    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var root = tree.GetRoot();
    var compilation = CreateCompilation(tree);
    var semanticModel = compilation.GetSemanticModel(tree);
    var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>()
      .Where(node => node.Identifier.ValueText is "left" or "right")
      .ToArray();
    var decisions = identifiers.Select(identifier => new RuleDecision(
      identifier,
      identifier,
      DecisionActionKind.Delete,
      "Replace parameter use with its default value.")).ToArray();
    var rewriter = new PrototypeRewriter();

    var direct = rewriter.Rewrite(root, semanticModel, decisions);
    var plan = rewriter.BuildPlan(root, semanticModel, decisions);
    var replayed = rewriter.ExecutePlan(source, "sample.cs", plan);
    var persisted = rewriter.ExecutePlan(source, "sample.cs", new RewritePlanFile("sample.cs", "unused", plan.Operations));
    var rewrittenSource = Assert.IsType<string>(replayed.RewrittenSource);
    var operations = Assert.IsAssignableFrom<IReadOnlyList<RewritePlanEdit>>(replayed.Operations);
    var errors = CreateCompilation(CSharpSyntaxTree.ParseText(rewrittenSource, path: "sample.cs"))
      .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

    Assert.Equal(direct.RewrittenSource, replayed.RewrittenSource);
    Assert.Equal(direct.RewrittenSource, persisted.RewrittenSource);
    Assert.Equal(direct.Edits, replayed.Edits);
    Assert.Equal(direct.Diff.ToString(), replayed.Diff.ToString());
    Assert.Equal(2, operations.Count);
    Assert.Empty(errors);
  }

  [Fact]
  public void PrototypeRewriter_ExecutePlan_WhenOperationsOverlap_ThrowsInvalidOperationException()
  {
    const string source = "class Sample { }";
    var plan = new PrototypeRewritePlan(
      new[]
      {
        new RewritePlanEdit(0, 5, "class", "struct"),
        new RewritePlanEdit(3, 5, "ss Sa", "")
      },
      Array.Empty<RewriteEdit>());

    var exception = Assert.Throws<InvalidOperationException>(() =>
      new PrototypeRewriter().ExecutePlan(source, "sample.cs", plan));

    Assert.Contains("Overlapping rewrite operations", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void PrototypeRewriter_ExecutePlan_WhenOriginalTextIsStale_ThrowsInvalidOperationException()
  {
    const string source = "class Sample { }";
    var plan = new PrototypeRewritePlan(
      new[] { new RewritePlanEdit(0, 5, "struct", "class") },
      Array.Empty<RewriteEdit>());

    var exception = Assert.Throws<InvalidOperationException>(() =>
      new PrototypeRewriter().ExecutePlan(source, "sample.cs", plan));

    Assert.Contains("original text does not match", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void PrototypeRewriter_Rewrite_ProducesDiffDocumentAndLegacyDiffText()
  {
    const string source = RewriteSources.SimpleValueReturnSource;

    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var root = tree.GetRoot();
    var compilation = CSharpCompilation.Create("sample", new[] { tree });
    var semanticModel = compilation.GetSemanticModel(tree);
    var returnStatement = root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
    var rewriter = new PrototypeRewriter();

    var result = rewriter.Rewrite(
      root,
      semanticModel,
      new[]
      {
        new RuleDecision(
          returnStatement,
          returnStatement,
          DecisionActionKind.Delete,
          "Delete the return.")
      });

    Assert.Single(result.Diff.Files);
    var file = Assert.Single(result.Diff.Files);
    var section = Assert.Single(file.Sections);
    Assert.Equal("sample.cs", file.FilePath);
    Assert.Equal(TextSpan.FromBounds(returnStatement.Span.Start, returnStatement.Span.End), section.Span);
    Assert.Contains("--- original #1 sample.cs", result.Diff);
    Assert.Contains("+++ rewritten #1", result.Diff);
    Assert.Contains("return default(int);", result.Diff);
  }

  [Fact]
  public void DiffBuilder_Combine_PreservesFileOrderAndSummary()
  {
    var builder = new DiffBuilder();
    var first = builder.Build(
      new[]
      {
        new RewriteEdit("b.cs", new TextSpan(10, 3), "old", "new")
      });
    var second = builder.Build(
      new[]
      {
        new RewriteEdit("a.cs", new TextSpan(1, 2), "x", string.Empty)
      });

    var combined = builder.Combine(new[] { first, second });

    Assert.Equal(new[] { "a.cs", "b.cs" }, combined.Files.Select(file => file.FilePath).ToArray());
    Assert.Equal(2, combined.Summary.FileCount);
    Assert.Equal(2, combined.Summary.EditCount);
    Assert.Equal(2, combined.Summary.SectionCount);
  }

  [Fact]
  public void TextDiffRenderer_RenderReadable_ProducesReadableHeadersAndSummary()
  {
    var builder = new DiffBuilder();
    var renderer = new TextDiffRenderer();
    var diff = builder.Build(
      new[]
      {
        new RewriteEdit("sample.cs", new TextSpan(2, 3), "old", "new")
      });

    var rendered = renderer.RenderReadable(diff);

    Assert.Contains("diff-summary files=1 edits=1", rendered);
    Assert.Contains("=== file sample.cs", rendered);
    Assert.Contains("edit #1 kind=Replace span=2..5", rendered);
    Assert.Contains("--- before", rendered);
    Assert.Contains("+++ after", rendered);
  }

  private static CSharpCompilation CreateCompilation(SyntaxTree tree)
  {
    return CSharpCompilation.Create(
      "RewriteContractTests",
      new[] { tree },
      new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  }
}

