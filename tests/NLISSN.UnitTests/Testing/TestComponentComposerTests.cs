using NLISSN.Infrastructure.Testing;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TestComponentComposerTests
{
  [Fact]
  public void Compose_DeclarationAndExpressionComponents_ReturnsValidStableFixture()
  {
    var components = new[]
    {
      new TestComponent(
        "type",
        TestComponentKind.Declaration,
        new[]
        {
          new TestComponentSource(
            "Demo.cs",
            "public static class Demo { public static bool Run() { return")
        }),
      new TestComponent(
        "expression",
        TestComponentKind.Expression,
        new[]
        {
          new TestComponentSource("Demo.cs", "true; } }")
        })
    };

    var composition = new TestComponentComposer().Compose(components);

    Assert.True(composition.IsSyntaxValid);
    var source = Assert.Single(composition.Sources);
    Assert.Equal("Demo.cs", source.RelativePath);
    Assert.Contains(
      "return" + Environment.NewLine + "true;",
      source.Text,
      StringComparison.Ordinal);
    Assert.Equal(
      new[] { "type", "expression" },
      composition.ProvenanceByFile["Demo.cs"]);
  }

  [Fact]
  public void Compose_InvalidCombinedFragments_ReturnsComponentDiagnostic()
  {
    var components = new[]
    {
      new TestComponent(
        "open-block",
        TestComponentKind.Statement,
        new[] { new TestComponentSource("Broken.cs", "public class Broken {") }),
      new TestComponent(
        "invalid-member",
        TestComponentKind.Expression,
        new[] { new TestComponentSource("Broken.cs", "return;") })
    };

    var composition = new TestComponentComposer().Compose(components);

    Assert.False(composition.IsSyntaxValid);
    Assert.NotEmpty(composition.SyntaxDiagnostics);
    Assert.All(composition.SyntaxDiagnostics, diagnostic => Assert.Equal("Broken.cs", diagnostic.RelativePath));
    Assert.Contains(composition.SyntaxDiagnostics, diagnostic =>
      diagnostic.Message.Contains("return", StringComparison.OrdinalIgnoreCase));
    Assert.Contains("invalid-member", composition.ProvenanceByFile["Broken.cs"]);
  }

  [Fact]
  public void Compose_DuplicateComponentIds_Throws()
  {
    var components = new[]
    {
      new TestComponent(
        "same",
        TestComponentKind.Statement,
        new[] { new TestComponentSource("A.cs", "class A {}") }),
      new TestComponent(
        "same",
        TestComponentKind.Statement,
        new[] { new TestComponentSource("B.cs", "class B {}") })
    };

    var exception = Assert.Throws<ArgumentException>(
      () => new TestComponentComposer().Compose(components));

    Assert.Contains("same", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Compose_MissingPrerequisite_ReportsCompositionDiagnostic()
  {
    var component = new TestComponent(
      "expression",
      TestComponentKind.Expression,
      new[] { new TestComponentSource("Demo.cs", "class Demo {}") },
      prerequisites: new[] { "missing-declaration" });

    var composition = new TestComponentComposer().Compose(new[] { component });

    Assert.False(composition.IsValid);
    var diagnostic = Assert.Single(composition.CompositionDiagnostics);
    Assert.Equal("TEST-COMPONENT-PREREQUISITE-001", diagnostic.Code);
    Assert.Equal("expression", diagnostic.ComponentId);
  }

  [Fact]
  public void Compose_CyclicPrerequisites_ReportsCompositionDiagnostic()
  {
    var components = new[]
    {
      new TestComponent(
        "first",
        TestComponentKind.Context,
        new[] { new TestComponentSource("Demo.cs", "class Demo {}") },
        prerequisites: new[] { "second" }),
      new TestComponent(
        "second",
        TestComponentKind.Context,
        new[] { new TestComponentSource("Demo.cs", "") },
        prerequisites: new[] { "first" })
    };

    var composition = new TestComponentComposer().Compose(components);

    Assert.False(composition.IsValid);
    var diagnostic = Assert.Single(composition.CompositionDiagnostics);
    Assert.Equal("TEST-COMPONENT-PREREQUISITE-002", diagnostic.Code);
    Assert.Contains("cycle", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Compose_PathTraversal_IsRejectedBeforeComposition()
  {
    Assert.Throws<ArgumentException>(() => new TestComponentSource("../outside.cs", "class Outside {}"));
    Assert.Throws<ArgumentException>(() => new TestComponentSource("nested/../../outside.cs", "class Outside {}"));
  }

  [Fact]
  public void Generate_ThreeComponents_ReturnsOnlySyntaxValidCompositionsWithPrerequisites()
  {
    var declaration = new TestComponent(
      "declaration",
      TestComponentKind.Declaration,
      new[]
      {
        new TestComponentSource("Demo.cs", "public static class Demo { public static bool Run() {")
      });
    var expression = new TestComponent(
      "expression",
      TestComponentKind.Expression,
      new[] { new TestComponentSource("Demo.cs", "return true; } }") },
      prerequisites: new[] { "declaration" });
    var invalid = new TestComponent(
      "invalid",
      TestComponentKind.Expression,
      new[] { new TestComponentSource("Demo.cs", "return;") });

    var compositions = new TestComponentCombinationGenerator().Generate(
      new[] { declaration, expression, invalid },
      componentCount: 2);

    var composition = Assert.Single(compositions);
    Assert.True(composition.IsValid);
    Assert.Equal(new[] { "declaration", "expression" }, composition.Components.Select(component => component.Id));
  }
}
