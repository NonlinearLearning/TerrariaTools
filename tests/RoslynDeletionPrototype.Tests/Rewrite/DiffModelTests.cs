using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynPrototype.Decision;
using RoslynPrototype.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Rewrite;

public sealed class DiffModelTests
{
  [Fact]
  public void PrototypeRewriter_Rewrite_ProducesDiffDocumentAndLegacyDiffText()
  {
    const string source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Run(int value)
        {
          return value + 1;
        }
      }
      """;

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
    Assert.Contains("<deleted>", result.Diff);
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
}

