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
    private readonly record struct SyntaxTraversalFrame(
      SyntaxNode Syntax,
      RoslynCpgNode Parent,
      RoslynCpgNode? Current,
      bool EmitTokens);

    internal void RunSyntaxPass(RoslynCpgBuildContext context)
    {
      VisitSyntaxIterative(
        context.Root,
        context.SyntaxTreeNode,
        context.Graph,
        context.SemanticModel,
        context.FilePath);
    }

    private void VisitSyntaxIterative(
      SyntaxNode syntax,
      RoslynCpgNode parent,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      string filePath)
    {
      var pending = new Stack<SyntaxTraversalFrame>();
      pending.Push(new SyntaxTraversalFrame(syntax, parent, Current: null, EmitTokens: false));

      while (pending.Count > 0)
      {
        var frame = pending.Pop();
        if (frame.EmitTokens)
        {
          EmitChildTokens(frame.Syntax, frame.Current!, graph, filePath);
          continue;
        }

        var syntaxNode = CreateSyntaxNode(frame.Syntax, frame.Parent, graph, semanticModel, filePath);
        pending.Push(new SyntaxTraversalFrame(frame.Syntax, frame.Parent, syntaxNode, EmitTokens: true));

        var childNodes = frame.Syntax.ChildNodes().ToArray();
        for (var index = childNodes.Length - 1; index >= 0; index -= 1)
        {
          pending.Push(new SyntaxTraversalFrame(childNodes[index], syntaxNode, Current: null, EmitTokens: false));
        }
      }
    }

    private RoslynCpgNode CreateSyntaxNode(
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
      return syntaxNode;
    }

    private static void EmitChildTokens(
      SyntaxNode syntax,
      RoslynCpgNode syntaxNode,
      RoslynCpgGraph graph,
      string filePath)
    {
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
