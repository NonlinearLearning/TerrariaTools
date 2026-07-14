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

    private readonly record struct SyntaxTypeResolution(
      ITypeSymbol? TypeSymbol,
      bool QueriedSemanticModel,
      bool ReusedReferencedSymbolType);

    private sealed class SyntaxPassMetrics
    {
      public long TraversalElapsedMilliseconds { get; set; }
      public long CreateSyntaxNodeElapsedMilliseconds { get; set; }
      public long EmitChildTokensElapsedMilliseconds { get; set; }
      public long AddDeclaredSymbolEdgesElapsedMilliseconds { get; set; }
      public long AddReferencedSymbolEdgesElapsedMilliseconds { get; set; }
      public long AddTypeInfoElapsedMilliseconds { get; set; }
      public long ResolveTypeInfoElapsedMilliseconds { get; set; }
      public long AddSyntaxTypeEdgesElapsedMilliseconds { get; set; }
      public long AddTypeReferenceEdgesElapsedMilliseconds { get; set; }
      public int TypeInfoQueryCount { get; set; }
      public int TypeInfoResolvedCount { get; set; }
      public int TypeInfoSymbolReuseCount { get; set; }
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
        metrics.ResolveTypeInfoElapsedMilliseconds,
        metrics.AddSyntaxTypeEdgesElapsedMilliseconds,
        metrics.AddTypeReferenceEdgesElapsedMilliseconds,
        metrics.TypeInfoQueryCount,
        metrics.TypeInfoResolvedCount,
        metrics.TypeInfoSymbolReuseCount,
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
      var referencedSymbol = CanReferenceSymbol(syntax) ? semanticModel.GetSymbolInfo(syntax).Symbol : null;
      AddReferencedSymbolEdges(syntax, syntaxNode, referencedSymbol, graph);
      referencedSymbolStopwatch.Stop();
      metrics.AddReferencedSymbolEdgesElapsedMilliseconds += referencedSymbolStopwatch.ElapsedMilliseconds;

      var shouldDeferToOperation = ShouldDeferSyntaxTypeToOperation(syntax);
      if (shouldDeferToOperation)
      {
        _pendingOperationSyntaxTypeNodes.Add(syntax);
      }

      var resolveTypeInfoStopwatch = Stopwatch.StartNew();
      var typeResolution = shouldDeferToOperation
        ? new SyntaxTypeResolution(null, QueriedSemanticModel: false, ReusedReferencedSymbolType: false)
        : ResolveSyntaxTypeSymbol(syntax, semanticModel, referencedSymbol);
      resolveTypeInfoStopwatch.Stop();
      metrics.ResolveTypeInfoElapsedMilliseconds += resolveTypeInfoStopwatch.ElapsedMilliseconds;
      metrics.TypeInfoQueryCount += typeResolution.QueriedSemanticModel ? 1 : 0;
      metrics.TypeInfoResolvedCount += typeResolution.TypeSymbol is null ? 0 : 1;
      metrics.TypeInfoSymbolReuseCount += typeResolution.ReusedReferencedSymbolType ? 1 : 0;

      var addTypeEdgesStopwatch = Stopwatch.StartNew();
      AddTypeEdges(syntaxNode, typeResolution.TypeSymbol, graph);
      addTypeEdgesStopwatch.Stop();
      metrics.AddSyntaxTypeEdgesElapsedMilliseconds += addTypeEdgesStopwatch.ElapsedMilliseconds;
      metrics.AddTypeInfoElapsedMilliseconds +=
        resolveTypeInfoStopwatch.ElapsedMilliseconds + addTypeEdgesStopwatch.ElapsedMilliseconds;

      var typeReferenceStopwatch = Stopwatch.StartNew();
      AddTypeReferenceEdges(syntax, syntaxNode, graph, semanticModel, typeResolution.TypeSymbol);
      typeReferenceStopwatch.Stop();
      metrics.AddTypeReferenceEdgesElapsedMilliseconds += typeReferenceStopwatch.ElapsedMilliseconds;
      return syntaxNode;
    }

    private SyntaxTypeResolution ResolveSyntaxTypeSymbol(
      SyntaxNode syntax,
      SemanticModel semanticModel,
      ISymbol? referencedSymbol)
    {
      if (syntax is not ExpressionSyntax and not TypeSyntax and not BaseTypeSyntax)
      {
        return new SyntaxTypeResolution(null, QueriedSemanticModel: false, ReusedReferencedSymbolType: false);
      }

      if (_options.EnableReferencedSymbolTypeReuse &&
          TryResolveTypeFromReferencedSymbol(referencedSymbol, out var reusedTypeSymbol))
      {
        return new SyntaxTypeResolution(
          reusedTypeSymbol,
          QueriedSemanticModel: false,
          ReusedReferencedSymbolType: true);
      }

      var typeSymbol = syntax switch
      {
        BaseTypeSyntax baseType => semanticModel.GetTypeInfo(baseType.Type).Type,
        _ => semanticModel.GetTypeInfo(syntax).Type,
      };
      return new SyntaxTypeResolution(typeSymbol, QueriedSemanticModel: true, ReusedReferencedSymbolType: false);
    }

    private static bool TryResolveTypeFromReferencedSymbol(ISymbol? symbol, out ITypeSymbol? typeSymbol)
    {
      typeSymbol = symbol switch
      {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        IEventSymbol eventSymbol => eventSymbol.Type,
        ITypeSymbol type => type,
        _ => null,
      };
      if (typeSymbol is null || typeSymbol.TypeKind is TypeKind.Dynamic or TypeKind.Error)
      {
        typeSymbol = null;
        return false;
      }

      return true;
    }

    private bool ShouldDeferSyntaxTypeToOperation(SyntaxNode syntax)
    {
      if (!_options.EnableOperationBackedSyntaxTypes || syntax is not ExpressionSyntax)
      {
        return false;
      }

      return syntax is IdentifierNameSyntax or
        MemberAccessExpressionSyntax or
        BinaryExpressionSyntax or
        ConditionalExpressionSyntax or
        InvocationExpressionSyntax or
        ElementAccessExpressionSyntax or
        ParenthesizedExpressionSyntax or
        CastExpressionSyntax or
        PrefixUnaryExpressionSyntax or
        PostfixUnaryExpressionSyntax;
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
