using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DeepSyntaxTraversalTests
{
  private const int ParenthesizedExpressionDepth = 512;

  [Fact]
  public void BuildFromSemanticModel_DeeplyNestedParenthesizedExpression_CompletesWithoutRecursiveSyntaxOverflow()
  {
    var tree = CSharpSyntaxTree.Create(CreateCompilationUnitRoot(), path: "deep-syntax.cs");
    var root = tree.GetRoot();
    var compilation = CreateCompilation(tree);
    var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

    var graph = new RoslynCpgBuilder().BuildFromSemanticModel(
      semanticModel,
      root,
      root.ToFullString(),
      "deep-syntax.cs");

    var parenthesizedNodes = graph.Nodes
      .Where(node =>
        node.Kind == RoslynCpgNodeKind.SyntaxNode &&
        string.Equals(node.DisplayKind, nameof(SyntaxKind.ParenthesizedExpression), StringComparison.Ordinal))
      .ToList();

    Assert.NotEmpty(graph.Nodes);
    Assert.NotEmpty(graph.Edges);
    Assert.Equal(ParenthesizedExpressionDepth, parenthesizedNodes.Count);
  }

  private static CompilationUnitSyntax CreateCompilationUnitRoot()
  {
    var expression = CreateDeepParenthesizedExpression(ParenthesizedExpressionDepth);
    var returnStatement = SyntaxFactory.ReturnStatement(expression);
    var method = SyntaxFactory.MethodDeclaration(
        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
        SyntaxFactory.Identifier("Run"))
      .AddModifiers(
        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
        SyntaxFactory.Token(SyntaxKind.StaticKeyword))
      .WithBody(SyntaxFactory.Block(returnStatement));
    var typeDeclaration = SyntaxFactory.ClassDeclaration("DeepSyntaxSample")
      .AddModifiers(
        SyntaxFactory.Token(SyntaxKind.InternalKeyword),
        SyntaxFactory.Token(SyntaxKind.StaticKeyword))
      .AddMembers(method);
    var namespaceDeclaration = SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.IdentifierName("Demo"))
      .AddMembers(typeDeclaration);
    return SyntaxFactory.CompilationUnit().AddMembers(namespaceDeclaration);
  }

  private static ExpressionSyntax CreateDeepParenthesizedExpression(int depth)
  {
    ExpressionSyntax current = SyntaxFactory.LiteralExpression(
      SyntaxKind.NumericLiteralExpression,
      SyntaxFactory.Literal(1));

    for (var index = 0; index < depth; index += 1)
    {
      current = SyntaxFactory.ParenthesizedExpression(current);
    }

    return current;
  }

  private static CSharpCompilation CreateCompilation(SyntaxTree tree)
  {
    return CSharpCompilation.Create(
      assemblyName: "DeepSyntaxTraversalTests",
      syntaxTrees: new[] { tree },
      references: new[]
      {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
      },
      options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  }
}
