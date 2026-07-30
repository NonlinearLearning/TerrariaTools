using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Rules;

namespace NLISSN.Rules;

/// 基于最小调用图可达性，命中从入口点不可达的方法声明。
public sealed class UnreachableMethodMarkRule : RuleDefinitionMark
{
    private static readonly RuleSemanticTag UnreachableMethodSemanticTag = new("UnreachableMethod");

    private static readonly RuleProducesContract UnreachableMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreachableMethodSemanticTag)
      });

    /// 规则稳定标识。
    public override string CapabilityId { get; } = "mark.unreachable-method";

    public override string RuleId { get; } = "DEL-DEAD-001";

    public override RuleProducesContract Produces => UnreachableMethodProduces;

    /// 规则的人类可读名称。
    public override string Name { get; } = "Match unreachable methods by graph reachability";

    /// 标记阶段允许产出的语法节点种类。
    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    // 从入口点沿调用图寻找可达方法，并把剩余方法声明标记为不可达删除候选。
    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        if (context.SemanticModel.Compilation.GetEntryPoint(CancellationToken.None) is null)
        {
            yield break;
        }

        var methodSyntaxById = BuildMethodSyntaxMap(context, root);
        var reachableMethods = FindReachableMethodIds(context, methodSyntaxById);

        foreach (var method in context.EnumerateMethodDeclarations(root))
        {
            if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var methodNode = FindMethodNodeBySymbol(context, methodSymbol);
            if (methodNode?.NodeId is null || reachableMethods.Contains(methodNode.NodeId.Value))
            {
                continue;
            }

            yield return new MarkRecord(
              RuleId,
              method,
              null,
              methodNode,
              "Method is unreachable from the discovered entry point.",
              SemanticTag: UnreachableMethodSemanticTag);
        }
    }

    private static HashSet<NodeId> FindReachableMethodIds(RuleContext context, IReadOnlyDictionary<NodeId, MethodDeclarationSyntax> methodSyntaxById)
    {
        var reachable = new HashSet<NodeId>();
        var worklist = new Queue<NLCPGNode>();
        var methodNodes = context.GetGraphNodesByKind(NLCPGNodeKind.Method);
        var symbolMethodToMethod = BuildSymbolMethodMap(context, methodNodes);

        var entrySymbol = context.SemanticModel.Compilation.GetEntryPoint(CancellationToken.None);
        if (entrySymbol is not null)
        {
            var entryNode = FindMethodNodeBySymbol(context, entrySymbol);
            if (entryNode?.NodeId is not null && reachable.Add(entryNode.NodeId.Value))
            {
                worklist.Enqueue(entryNode);
            }
        }

        if (reachable.Count == 0)
        {
            foreach (var entryMethod in methodNodes.Where(IsEntryMethod))
            {
                if (entryMethod.NodeId.HasValue && reachable.Add(entryMethod.NodeId.Value))
                {
                    worklist.Enqueue(entryMethod);
                }
            }
        }

        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            if (!current.NodeId.HasValue || !methodSyntaxById.TryGetValue(current.NodeId.Value, out var methodSyntax))
            {
                continue;
            }

            foreach (var callSiteNode in GetCallSitesForMethod(context, methodSyntax))
            {
                if (!callSiteNode.NodeId.HasValue)
                {
                    continue;
                }

                foreach (var targetSymbolNode in GetOutgoingTargets(context, callSiteNode.NodeId.Value, NLCPGEdgeKind.CallTargets)
                           .Where(node => node.Kind == NLCPGNodeKind.SymbolMethod))
                {
                    if (!targetSymbolNode.NodeId.HasValue ||
                        !symbolMethodToMethod.TryGetValue(targetSymbolNode.NodeId.Value, out var targetMethodNode) ||
                        !targetMethodNode.NodeId.HasValue)
                    {
                        continue;
                    }

                    if (reachable.Add(targetMethodNode.NodeId.Value))
                    {
                        worklist.Enqueue(targetMethodNode);
                    }
                }
            }
        }

        return reachable;
    }

    /// 把方法符号节点映射回对应的方法抽象节点，便于沿调用目标回到方法级可达性。
    private static IReadOnlyDictionary<NodeId, NLCPGNode> BuildSymbolMethodMap(RuleContext context, IReadOnlyList<NLCPGNode> methodNodes)
    {
        var methodByLocation = methodNodes
          .Where(node => node.NodeId.HasValue && node.FilePath is not null && node.SpanStart is not null && node.SpanEnd is not null)
          .ToDictionary(node => BuildLocationKey(node.FilePath!, node.SpanStart!.Value, node.SpanEnd!.Value), StringComparer.Ordinal);

        return context.GetGraphNodesByKind(NLCPGNodeKind.SymbolMethod)
          .Where(node => node.NodeId.HasValue && node.FilePath is not null && node.SpanStart is not null && node.SpanEnd is not null)
          .Select(node => new { SymbolNode = node, MethodNode = ResolveMethodNode(node, methodByLocation) })
          .Where(item => item.MethodNode is not null)
          .ToDictionary(item => item.SymbolNode.NodeId!.Value, item => item.MethodNode!);
    }

    private static IEnumerable<NLCPGNode> GetCallSitesForMethod(RuleContext context, MethodDeclarationSyntax methodSyntax)
    {
        foreach (var callSite in context.GetGraphNodesByKind(NLCPGNodeKind.CallSite))
        {
            if (IsInsideMethod(callSite, methodSyntax))
            {
                yield return callSite;
            }
        }
    }

    private static IEnumerable<NLCPGNode> GetOutgoingTargets(RuleContext context, NodeId sourceNodeId, NLCPGEdgeKind edgeKind)
    {
        var targetIds = context.GetGraphEdgesByKind(sourceNodeId, edgeKind)
          .Select(edge => edge.TargetNodeId)
          .ToHashSet();

        foreach (var targetId in targetIds)
        {
            var node = context.FindGraphNodeById(targetId);
            if (node is not null)
            {
                yield return node;
            }
        }
    }

    private static bool IsInsideMethod(NLCPGNode node, MethodDeclarationSyntax methodSyntax)
    {
        if (node.FilePath is null || methodSyntax.SyntaxTree.FilePath is null)
        {
            return false;
        }

        if (!string.Equals(node.FilePath, methodSyntax.SyntaxTree.FilePath, StringComparison.Ordinal))
        {
            return false;
        }

        if (node.SpanStart is null || node.SpanEnd is null)
        {
            return false;
        }

        return node.SpanStart.Value >= methodSyntax.SpanStart && node.SpanEnd.Value <= methodSyntax.Span.End;
    }

    private static bool IsEntryMethod(NLCPGNode node)
    {
        return node.Kind == NLCPGNodeKind.Method &&
          string.Equals(node.Name, "Main", StringComparison.Ordinal);
    }

    private static NLCPGNode? FindMethodNodeBySymbol(RuleContext context, IMethodSymbol methodSymbol)
    {
        var location = methodSymbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null || location.SourceTree?.FilePath is not string filePath)
        {
            return null;
        }

        return context.GetGraphNodesByKind(NLCPGNodeKind.Method)
          .FirstOrDefault(node =>
            string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
            node.SpanStart == location.SourceSpan.Start &&
            node.SpanEnd == location.SourceSpan.End &&
            string.Equals(node.Name, methodSymbol.Name, StringComparison.Ordinal));
    }

    /// 为每个方法抽象节点建立到源码方法声明的映射。
    private static IReadOnlyDictionary<NodeId, MethodDeclarationSyntax> BuildMethodSyntaxMap(RuleContext context, SyntaxNode root)
    {
        var map = new Dictionary<NodeId, MethodDeclarationSyntax>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            if (methodSymbol is null)
            {
                continue;
            }

            var methodNode = FindMethodNodeBySymbol(context, methodSymbol);
            if (methodNode?.NodeId is not null)
            {
                map[methodNode.NodeId.Value] = method;
            }
        }

        return map;
    }

    private static string BuildLocationKey(string filePath, int spanStart, int spanEnd)
    {
        return $"{filePath}|{spanStart}|{spanEnd}";
    }

    private static NLCPGNode? ResolveMethodNode(NLCPGNode symbolNode, IReadOnlyDictionary<string, NLCPGNode> methodByLocation)
    {
        if (symbolNode.FilePath is null || symbolNode.SpanStart is null || symbolNode.SpanEnd is null)
        {
            return null;
        }

        methodByLocation.TryGetValue(
          BuildLocationKey(symbolNode.FilePath, symbolNode.SpanStart.Value, symbolNode.SpanEnd.Value),
          out var methodNode);
        return methodNode;
    }
}
