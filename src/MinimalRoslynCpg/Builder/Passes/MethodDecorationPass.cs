using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes
{
  internal sealed class MethodDecorationPass : IRoslynCpgPass
  {
    internal static MethodDecorationPass Instance { get; } = new();

    private MethodDecorationPass()
    {
    }

    public string Name => nameof(MethodDecorationPass);

    public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
    {
      builder.RunMethodDecorationPass(context);
    }
  }
}

namespace MinimalRoslynCpg.Builder
{
  public sealed partial class RoslynCpgBuilder
  {
    internal void RunMethodDecorationPass(RoslynCpgBuildContext context)
    {
      var methodDeclarations = context.Root.DescendantNodes()
        .OfType<BaseMethodDeclarationSyntax>()
        .Cast<SyntaxNode>()
        .Concat(context.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        .Concat(context.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        .ToArray();
      foreach (var syntax in methodDeclarations)
      {
        if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode) ||
            context.SemanticModel.GetDeclaredSymbol(syntax) is not IMethodSymbol methodSymbol)
        {
          continue;
        }

        AddMethodAbstractions(syntaxNode, methodSymbol, context.Graph);
      }

      _methodDecorationTelemetry = new RoslynCpgMethodDecorationTelemetry(
        SyntaxNodeCount: methodDeclarations.Length,
        DeclaredSymbolQueryCount: methodDeclarations.Length);
    }

    private void AddMethodAbstractions(RoslynCpgNode syntaxNode, IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
      var methodNode = GetOrCreateMethodNode(methodSymbol, graph);
      var methodSymbolNode = GetOrCreateSymbolNode(methodSymbol, graph);
      graph.AddEdge(syntaxNode, methodNode, RoslynCpgEdgeKind.SyntaxChild);
      graph.AddEdge(methodNode, methodSymbolNode, RoslynCpgEdgeKind.DeclaresSymbol);

      if (methodSymbol.ReturnType is not null)
      {
        var returnTypeNode = GetOrCreateSymbolNode(methodSymbol.ReturnType, graph);
        graph.AddEdge(methodNode, returnTypeNode, RoslynCpgEdgeKind.ReturnsType);
        AddEvalTypeEdge(methodNode, methodSymbol.ReturnType, graph);

        var methodReturnNode = GetOrCreateMethodReturnNode(methodSymbol, graph);
        graph.AddEdge(methodNode, methodReturnNode, RoslynCpgEdgeKind.ParameterLink);
        graph.AddEdge(methodReturnNode, returnTypeNode, RoslynCpgEdgeKind.EvalType);
      }

      var entryNode = GetOrCreateMethodEntryNode(methodSymbol, graph);
      var exitNode = GetOrCreateMethodExitNode(methodSymbol, graph);
      graph.AddEdge(methodNode, entryNode, RoslynCpgEdgeKind.SyntaxChild);
      graph.AddEdge(methodNode, exitNode, RoslynCpgEdgeKind.SyntaxChild);

      foreach (var parameter in methodSymbol.Parameters)
      {
        var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
        graph.AddEdge(methodNode, parameterNode, RoslynCpgEdgeKind.ParameterLink);
      }
    }

    private RoslynCpgNode GetOrCreateMethodNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
      methodSymbol = CanonicalMethodSymbol(methodSymbol);
      var key = $"method:{SymbolId(methodSymbol)}";
      var methodSymbolKey = SymbolId(methodSymbol);
      if (_methodNodes.TryGetValue(key, out var existing))
      {
        return existing;
      }

      var methodNode = graph.AddNode(new RoslynCpgNode(
        Kind: RoslynCpgNodeKind.Method,
        DisplayKind: nameof(RoslynCpgNodeKind.Method),
        Name: ComposeMethodName(methodSymbol),
        FullName: ComposeMethodFullName(methodSymbol),
        Signature: ComposeMethodSignature(methodSymbol),
        DispatchKind: ComposeMethodDispatchKind(methodSymbol),
        TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
        FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
        SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
        SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
      _symbolKeysByNode[methodNode] = methodSymbolKey;
      _methodNodes[key] = methodNode;
      return methodNode;
    }

    private RoslynCpgNode GetOrCreateMethodParameterNode(IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, RoslynCpgGraph graph)
    {
      methodSymbol = CanonicalMethodSymbol(methodSymbol);
      var key = $"methodparam:{SymbolId(methodSymbol)}:{parameterSymbol.Ordinal}";
      var methodSymbolKey = SymbolId(methodSymbol);
      if (_methodParameterNodes.TryGetValue(key, out var existing))
      {
        return existing;
      }

      var parameterNode = graph.AddNode(new RoslynCpgNode(
        Kind: RoslynCpgNodeKind.MethodParameter,
        DisplayKind: nameof(RoslynCpgNodeKind.MethodParameter),
        Name: parameterSymbol.Name,
        FullName: $"{ComposeMethodFullName(methodSymbol)}#{parameterSymbol.Ordinal}:{parameterSymbol.Name}",
        Signature: ComposeTypeFullName(parameterSymbol.Type),
        TypeFullName: ComposeTypeFullName(parameterSymbol.Type),
        FilePath: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
        SpanStart: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
        SpanEnd: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
      _methodOwnerSymbolKeysByBoundaryNode[parameterNode] = methodSymbolKey;
      _methodParameterOrdinalsByNode[parameterNode] = parameterSymbol.Ordinal;
      _methodParameterNodes[key] = parameterNode;

      var parameterSymbolNode = GetOrCreateSymbolNode(parameterSymbol, graph);
      graph.AddEdge(parameterNode, parameterSymbolNode, RoslynCpgEdgeKind.Ref);
      AddEvalTypeEdge(parameterNode, parameterSymbol.Type, graph);
      return parameterNode;
    }

    private RoslynCpgNode GetOrCreateMethodReturnNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
      methodSymbol = CanonicalMethodSymbol(methodSymbol);
      var key = $"methodreturn:{SymbolId(methodSymbol)}";
      var methodSymbolKey = SymbolId(methodSymbol);
      if (_methodReturnNodes.TryGetValue(key, out var existing))
      {
        return existing;
      }

      var returnNode = graph.AddNode(new RoslynCpgNode(
        Kind: RoslynCpgNodeKind.MethodReturn,
        DisplayKind: nameof(RoslynCpgNodeKind.MethodReturn),
        Name: $"{ComposeMethodName(methodSymbol)}:return",
        FullName: $"{ComposeMethodFullName(methodSymbol)}:return",
        Signature: ComposeTypeFullName(methodSymbol.ReturnType),
        TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
        FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
        SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
        SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
      _methodOwnerSymbolKeysByBoundaryNode[returnNode] = methodSymbolKey;
      _methodReturnNodes[key] = returnNode;
      return returnNode;
    }

    private RoslynCpgNode GetOrCreateMethodEntryNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
      methodSymbol = CanonicalMethodSymbol(methodSymbol);
      var key = $"methodentry:{SymbolId(methodSymbol)}";
      if (_methodEntryNodes.TryGetValue(key, out var existing))
      {
        return existing;
      }

      var entryNode = graph.AddNode(new RoslynCpgNode(
        Kind: RoslynCpgNodeKind.MethodEntry,
        DisplayKind: nameof(RoslynCpgNodeKind.MethodEntry),
        Name: $"{ComposeMethodName(methodSymbol)}:entry",
        FullName: $"{ComposeMethodFullName(methodSymbol)}:entry",
        Signature: ComposeMethodSignature(methodSymbol),
        TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
        FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
        SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
        SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start));
      _methodEntryNodes[key] = entryNode;
      return entryNode;
    }

    private RoslynCpgNode GetOrCreateMethodExitNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
      methodSymbol = CanonicalMethodSymbol(methodSymbol);
      var key = $"methodexit:{SymbolId(methodSymbol)}";
      if (_methodExitNodes.TryGetValue(key, out var existing))
      {
        return existing;
      }

      var exitNode = graph.AddNode(new RoslynCpgNode(
        Kind: RoslynCpgNodeKind.MethodExit,
        DisplayKind: nameof(RoslynCpgNodeKind.MethodExit),
        Name: $"{ComposeMethodName(methodSymbol)}:exit",
        FullName: $"{ComposeMethodFullName(methodSymbol)}:exit",
        Signature: ComposeMethodSignature(methodSymbol),
        TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
        FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
        SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
        SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
      _methodExitNodes[key] = exitNode;
      return exitNode;
    }
  }
}
