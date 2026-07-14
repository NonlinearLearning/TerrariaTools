using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Diagnostics;

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

    private sealed class SyntaxPassMetrics
    {
      public long TraversalElapsedMilliseconds { get; set; }
      public long CreateSyntaxNodeElapsedMilliseconds { get; set; }
      public long EmitChildTokensElapsedMilliseconds { get; set; }
      public long AddDeclaredSymbolEdgesElapsedMilliseconds { get; set; }
      public long AddReferencedSymbolEdgesElapsedMilliseconds { get; set; }
      public long AddTypeInfoElapsedMilliseconds { get; set; }
      public long AddTypeReferenceEdgesElapsedMilliseconds { get; set; }
      public int SyntaxNodeCount { get; set; }
      public int SyntaxTokenCount { get; set; }
    }

    internal void RunSyntaxPass(RoslynCpgBuildContext context)
    {
      var totalStopwatch = Stopwatch.StartNew();
      var metrics = new SyntaxPassMetrics();
      VisitSyntaxIterative(
        context.Root,
        context.SyntaxTreeNode,
        context.Graph,
        context.SemanticModel,
        context.FilePath,
        metrics);
      totalStopwatch.Stop();
      _syntaxPassTelemetry = new RoslynCpgSyntaxPassTelemetry(
        totalStopwatch.ElapsedMilliseconds,
        metrics.TraversalElapsedMilliseconds,
        metrics.CreateSyntaxNodeElapsedMilliseconds,
        metrics.EmitChildTokensElapsedMilliseconds,
        metrics.AddDeclaredSymbolEdgesElapsedMilliseconds,
        metrics.AddReferencedSymbolEdgesElapsedMilliseconds,
        metrics.AddTypeInfoElapsedMilliseconds,
        metrics.AddTypeReferenceEdgesElapsedMilliseconds,
        metrics.SyntaxNodeCount,
        metrics.SyntaxTokenCount);
    }

    private void VisitSyntaxIterative(
      SyntaxNode syntax,
      RoslynCpgNode parent,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      string filePath,
      SyntaxPassMetrics metrics)
    {
      var traversalStopwatch = Stopwatch.StartNew();
      var pending = new Stack<SyntaxTraversalFrame>();
      pending.Push(new SyntaxTraversalFrame(syntax, parent, Current: null, EmitTokens: false));

      while (pending.Count > 0)
      {
        var frame = pending.Pop();
        if (frame.EmitTokens)
        {
          EmitChildTokens(frame.Syntax, frame.Current!, graph, filePath, metrics);
          continue;
        }

        var syntaxNode = CreateSyntaxNode(frame.Syntax, frame.Parent, graph, semanticModel, filePath, metrics);
        pending.Push(new SyntaxTraversalFrame(frame.Syntax, frame.Parent, syntaxNode, EmitTokens: true));

        var childNodes = frame.Syntax.ChildNodes().ToArray();
        for (var index = childNodes.Length - 1; index >= 0; index -= 1)
        {
          pending.Push(new SyntaxTraversalFrame(childNodes[index], syntaxNode, Current: null, EmitTokens: false));
        }
      }

      traversalStopwatch.Stop();
      metrics.TraversalElapsedMilliseconds = traversalStopwatch.ElapsedMilliseconds;
    }

    private RoslynCpgNode CreateSyntaxNode(
      SyntaxNode syntax,
      RoslynCpgNode parent,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      string filePath,
      SyntaxPassMetrics metrics)
    {
      var createNodeStopwatch = Stopwatch.StartNew();
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
      createNodeStopwatch.Stop();
      metrics.CreateSyntaxNodeElapsedMilliseconds += createNodeStopwatch.ElapsedMilliseconds;
      metrics.SyntaxNodeCount += 1;
      _syntaxNodes[syntax] = syntaxNode;
      graph.AddEdge(parent, syntaxNode, RoslynCpgEdgeKind.SyntaxChild);

      var declaredSymbolStopwatch = Stopwatch.StartNew();
      AddDeclaredSymbolEdges(syntax, syntaxNode, graph, semanticModel);
      declaredSymbolStopwatch.Stop();
      metrics.AddDeclaredSymbolEdgesElapsedMilliseconds += declaredSymbolStopwatch.ElapsedMilliseconds;

      var referencedSymbolStopwatch = Stopwatch.StartNew();
      AddReferencedSymbolEdges(syntax, syntaxNode, graph, semanticModel);
      referencedSymbolStopwatch.Stop();
      metrics.AddReferencedSymbolEdgesElapsedMilliseconds += referencedSymbolStopwatch.ElapsedMilliseconds;

      var typeInfoStopwatch = Stopwatch.StartNew();
      AddTypeEdges(syntaxNode, semanticModel.GetTypeInfo(syntax).Type, graph);
      typeInfoStopwatch.Stop();
      metrics.AddTypeInfoElapsedMilliseconds += typeInfoStopwatch.ElapsedMilliseconds;

      var typeReferenceStopwatch = Stopwatch.StartNew();
      AddTypeReferenceEdges(syntax, syntaxNode, graph, semanticModel);
      typeReferenceStopwatch.Stop();
      metrics.AddTypeReferenceEdgesElapsedMilliseconds += typeReferenceStopwatch.ElapsedMilliseconds;
      return syntaxNode;
    }

    private static void EmitChildTokens(
      SyntaxNode syntax,
      RoslynCpgNode syntaxNode,
      RoslynCpgGraph graph,
      string filePath,
      SyntaxPassMetrics metrics)
    {
      var tokenStopwatch = Stopwatch.StartNew();
      var tokenCount = 0;
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
        tokenCount += 1;
      }

      tokenStopwatch.Stop();
      metrics.EmitChildTokensElapsedMilliseconds += tokenStopwatch.ElapsedMilliseconds;
      metrics.SyntaxTokenCount += tokenCount;
    }
  }
}
