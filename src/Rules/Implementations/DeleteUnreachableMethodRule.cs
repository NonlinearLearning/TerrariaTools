using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace Rules;

public sealed class DeleteUnreachableMethodRule : IDeletionRule
{
  public RuleMetadata Metadata { get; } = new(
    "DEL-DEAD-001",
    "Match unreachable methods by graph reachability",
    true);

  public IReadOnlyList<SyntaxKind> AllowedNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
  {
    if (context.SemanticModel.Compilation.GetEntryPoint(CancellationToken.None) is null) {
      yield break;
    }

    var methodSyntaxById = BuildMethodSyntaxMap(context, root);
    var reachableMethods = FindReachableMethodIds(context, methodSyntaxById);

    foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
      if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol) {
        continue;
      }

      var methodNode = FindMethodNodeBySymbol(context.Graph, methodSymbol);
      if (methodNode is null || reachableMethods.Contains(methodNode.Id)) {
        continue;
      }

      yield return new MarkRecord(
        Metadata.RuleId,
        method,
        null,
        methodNode,
        "Method is unreachable from the discovered entry point.");
    }
  }

  public IEnumerable<PropagatedMarkRecord> Propagate(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks)
  {
    return Enumerable.Empty<PropagatedMarkRecord>();
  }

  private static HashSet<string> FindReachableMethodIds(
    RuleContext context,
    IReadOnlyDictionary<string, MethodDeclarationSyntax> methodSyntaxById)
  {
    var graph = context.Graph;
    var reachable = new HashSet<string>(StringComparer.Ordinal);
    var worklist = new Queue<RoslynCpgNode>();
    var methodNodes = graph.NodesByKind(RoslynCpgNodeKind.Method).ToList();
    var symbolMethodToMethod = BuildSymbolMethodMap(graph, methodNodes);

    var entrySymbol = context.SemanticModel.Compilation.GetEntryPoint(CancellationToken.None);
    if (entrySymbol is not null) {
      var entryNode = FindMethodNodeBySymbol(graph, entrySymbol);
      if (entryNode is not null && reachable.Add(entryNode.Id)) {
        worklist.Enqueue(entryNode);
      }
    }

    if (reachable.Count == 0) {
      foreach (var entryMethod in methodNodes.Where(IsEntryMethod)) {
        if (reachable.Add(entryMethod.Id)) {
          worklist.Enqueue(entryMethod);
        }
      }
    }

    while (worklist.Count > 0) {
      var current = worklist.Dequeue();
      if (!methodSyntaxById.TryGetValue(current.Id, out var methodSyntax)) {
        continue;
      }

      foreach (var callSiteNode in GetCallSitesForMethod(graph, methodSyntax)) {
        foreach (var targetSymbolNode in GetOutgoingTargets(graph, callSiteNode.Id, RoslynCpgEdgeKind.CallTargets)
                   .Where(node => node.Kind == RoslynCpgNodeKind.SymbolMethod)) {
          if (!symbolMethodToMethod.TryGetValue(targetSymbolNode.Id, out var targetMethodNode)) {
            continue;
          }

          if (reachable.Add(targetMethodNode.Id)) {
            worklist.Enqueue(targetMethodNode);
          }
        }
      }
    }

    return reachable;
  }

  private static IReadOnlyDictionary<string, RoslynCpgNode> BuildSymbolMethodMap(
    RoslynCpgGraph graph,
    IReadOnlyList<RoslynCpgNode> methodNodes)
  {
    var methodByLocation = methodNodes
      .Where(node => node.FilePath is not null && node.SpanStart is not null && node.SpanEnd is not null)
      .ToDictionary(node => BuildLocationKey(node.FilePath!, node.SpanStart!.Value, node.SpanEnd!.Value), StringComparer.Ordinal);

    return graph.NodesByKind(RoslynCpgNodeKind.SymbolMethod)
      .Where(node => node.FilePath is not null && node.SpanStart is not null && node.SpanEnd is not null)
      .Select(node => new { SymbolNode = node, MethodNode = ResolveMethodNode(node, methodByLocation) })
      .Where(item => item.MethodNode is not null)
      .ToDictionary(item => item.SymbolNode.Id, item => item.MethodNode!, StringComparer.Ordinal);
  }

  private static IEnumerable<RoslynCpgNode> GetCallSitesForMethod(RoslynCpgGraph graph, MethodDeclarationSyntax methodSyntax)
  {
    foreach (var callSite in graph.NodesByKind(RoslynCpgNodeKind.CallSite)) {
      if (IsInsideMethod(callSite, methodSyntax)) {
        yield return callSite;
      }
    }
  }

  private static IEnumerable<RoslynCpgNode> GetOutgoingTargets(
    RoslynCpgGraph graph,
    string sourceId,
    RoslynCpgEdgeKind edgeKind)
  {
    var targetIds = graph.Edges
      .Where(edge => edge.SourceId == sourceId && edge.Kind == edgeKind)
      .Select(edge => edge.TargetId)
      .ToHashSet(StringComparer.Ordinal);

    foreach (var node in graph.Nodes) {
      if (targetIds.Contains(node.Id)) {
        yield return node;
      }
    }
  }

  private static bool IsInsideMethod(RoslynCpgNode node, MethodDeclarationSyntax methodSyntax)
  {
    if (node.FilePath is null || methodSyntax.SyntaxTree.FilePath is null) {
      return false;
    }

    if (!string.Equals(node.FilePath, methodSyntax.SyntaxTree.FilePath, StringComparison.Ordinal)) {
      return false;
    }

    if (node.SpanStart is null || node.SpanEnd is null) {
      return false;
    }

    return node.SpanStart.Value >= methodSyntax.SpanStart && node.SpanEnd.Value <= methodSyntax.Span.End;
  }

  private static bool IsEntryMethod(RoslynCpgNode node)
  {
    return node.Kind == RoslynCpgNodeKind.Method &&
      string.Equals(node.Name, "Main", StringComparison.Ordinal);
  }

  private static RoslynCpgNode? FindMethodNodeBySymbol(RoslynCpgGraph graph, IMethodSymbol methodSymbol)
  {
    var location = methodSymbol.Locations.FirstOrDefault(location => location.IsInSource);
    if (location is null || location.SourceTree?.FilePath is not string filePath) {
      return null;
    }

    return graph.NodesByKind(RoslynCpgNodeKind.Method)
      .FirstOrDefault(node =>
        string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
        node.SpanStart == location.SourceSpan.Start &&
        node.SpanEnd == location.SourceSpan.End &&
        string.Equals(node.Name, methodSymbol.Name, StringComparison.Ordinal));
  }

  private static IReadOnlyDictionary<string, MethodDeclarationSyntax> BuildMethodSyntaxMap(
    RuleContext context,
    SyntaxNode root)
  {
    var map = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
    foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
      var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
      if (methodSymbol is null) {
        continue;
      }

      var methodNode = FindMethodNodeBySymbol(context.Graph, methodSymbol);
      if (methodNode is not null) {
        map[methodNode.Id] = method;
      }
    }

    return map;
  }

  private static string BuildLocationKey(string filePath, int spanStart, int spanEnd)
  {
    return $"{filePath}|{spanStart}|{spanEnd}";
  }

  private static RoslynCpgNode? ResolveMethodNode(
    RoslynCpgNode symbolNode,
    IReadOnlyDictionary<string, RoslynCpgNode> methodByLocation)
  {
    if (symbolNode.FilePath is null || symbolNode.SpanStart is null || symbolNode.SpanEnd is null) {
      return null;
    }

    methodByLocation.TryGetValue(
      BuildLocationKey(symbolNode.FilePath, symbolNode.SpanStart.Value, symbolNode.SpanEnd.Value),
      out var methodNode);
    return methodNode;
  }
}
