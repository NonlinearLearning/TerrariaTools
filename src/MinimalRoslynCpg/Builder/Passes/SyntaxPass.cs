using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes
{
  internal sealed class SyntaxPass : IRoslynCpgPass
  {
    internal static SyntaxPass Instance { get; } = new();

    private SyntaxPass()
    {
    }

    public string Name => nameof(SyntaxPass);

    public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
    {
      builder.RunSyntaxPass(context);
    }
  }
}

namespace MinimalRoslynCpg.Builder
{
  public sealed partial class RoslynCpgBuilder
  {
    internal void RunSyntaxPass(RoslynCpgBuildContext context)
    {
      VisitSyntax(
        context.Root,
        context.SyntaxTreeNode,
        context.Graph,
        context.SemanticModel,
        context.FilePath);
    }

    private void VisitSyntax(
      SyntaxNode syntax,
      RoslynCpgNode parent,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      string filePath)
    {
      var syntaxNode = graph.AddNode(new RoslynCpgNode(
        Id: SyntaxId(syntax, filePath),
        Kind: RoslynCpgNodeKind.SyntaxNode,
        DisplayKind: syntax.Kind().ToString(),
        Name: syntax switch
        {
          BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
          BaseMethodDeclarationSyntax methodDeclaration => NameOfMethod(methodDeclaration),
          VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
          ParameterSyntax parameter => parameter.Identifier.ValueText,
          _ => null,
        },
        FilePath: filePath,
        SpanStart: syntax.SpanStart,
        SpanEnd: syntax.Span.End,
        Text: Shorten(syntax.ToString())));
      _syntaxNodes[syntax] = syntaxNode;
      graph.AddEdge(parent, syntaxNode, RoslynCpgEdgeKind.SyntaxChild);

      AddDeclaredSymbolEdges(syntax, syntaxNode, graph, semanticModel);
      AddReferencedSymbolEdges(syntax, syntaxNode, graph, semanticModel);
      AddTypeEdges(syntaxNode, semanticModel.GetTypeInfo(syntax).Type, graph);
      AddTypeReferenceEdges(syntax, syntaxNode, graph, semanticModel);

      foreach (var childNode in syntax.ChildNodes())
      {
        VisitSyntax(childNode, syntaxNode, graph, semanticModel, filePath);
      }

      foreach (var childToken in syntax.ChildTokens())
      {
        var tokenNode = graph.AddNode(new RoslynCpgNode(
          Id: TokenId(childToken, filePath),
          Kind: RoslynCpgNodeKind.SyntaxToken,
          DisplayKind: childToken.Kind().ToString(),
          Name: childToken.ValueText.Length > 0 ? childToken.ValueText : childToken.Text,
          FilePath: filePath,
          SpanStart: childToken.SpanStart,
          SpanEnd: childToken.Span.End,
          Text: childToken.Text));
        graph.AddEdge(syntaxNode, tokenNode, RoslynCpgEdgeKind.TokenChild);
      }
    }
  }
}
