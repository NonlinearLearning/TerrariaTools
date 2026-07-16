using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Analysis;
using RoslynPrototype.Marking;
using Rules;

namespace Rules;

/// <summary>
/// 基于最小调用图可达性，命中从入口点不可达的方法声明。
/// </summary>
public sealed class DeleteUnreachableMethodRule : RuleDefinitionMark
{
    /// <summary>
    /// 规则稳定标识。
    /// </summary>
    public override string RuleId { get; } = "DEL-DEAD-001";

    /// <summary>
    /// 规则的人类可读名称。
    /// </summary>
    public override string Name { get; } = "Match unreachable methods by graph reachability";

    /// <summary>
    /// 标记阶段允许产出的语法节点种类。
    /// </summary>
    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    /// <summary>
    /// 从入口点出发计算可达方法集合，并命中剩余不可达的方法声明。
    /// </summary>
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
              "Method is unreachable from the discovered entry point.");
        }
    }

    /// <summary>
    /// 从入口方法出发，沿调用图做最小广度优先遍历，收集可达方法。
    /// </summary>
    private static HashSet<NodeId> FindReachableMethodIds(RuleContext context, IReadOnlyDictionary<NodeId, MethodDeclarationSyntax> methodSyntaxById)
    {
        var reachable = new HashSet<NodeId>();
        var worklist = new Queue<RoslynCpgNode>();
        var methodNodes = context.GetGraphNodesByKind(RoslynCpgNodeKind.Method);
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

                foreach (var targetSymbolNode in GetOutgoingTargets(context, callSiteNode.NodeId.Value, RoslynCpgEdgeKind.CallTargets)
                           .Where(node => node.Kind == RoslynCpgNodeKind.SymbolMethod))
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

    /// <summary>
    /// 把方法符号节点映射回对应的方法抽象节点，便于沿调用目标回到方法级可达性。
    /// </summary>
    private static IReadOnlyDictionary<NodeId, RoslynCpgNode> BuildSymbolMethodMap(RuleContext context, IReadOnlyList<RoslynCpgNode> methodNodes)
    {
        var methodByLocation = methodNodes
          .Where(node => node.NodeId.HasValue && node.FilePath is not null && node.SpanStart is not null && node.SpanEnd is not null)
          .ToDictionary(node => BuildLocationKey(node.FilePath!, node.SpanStart!.Value, node.SpanEnd!.Value), StringComparer.Ordinal);

        return context.GetGraphNodesByKind(RoslynCpgNodeKind.SymbolMethod)
          .Where(node => node.NodeId.HasValue && node.FilePath is not null && node.SpanStart is not null && node.SpanEnd is not null)
          .Select(node => new { SymbolNode = node, MethodNode = ResolveMethodNode(node, methodByLocation) })
          .Where(item => item.MethodNode is not null)
          .ToDictionary(item => item.SymbolNode.NodeId!.Value, item => item.MethodNode!);
    }

    /// <summary>
    /// 找出方法体范围内的所有调用点节点。
    /// </summary>
    private static IEnumerable<RoslynCpgNode> GetCallSitesForMethod(RuleContext context, MethodDeclarationSyntax methodSyntax)
    {
        foreach (var callSite in context.GetGraphNodesByKind(RoslynCpgNodeKind.CallSite))
        {
            if (IsInsideMethod(callSite, methodSyntax))
            {
                yield return callSite;
            }
        }
    }

    /// <summary>
    /// 读取某个图节点沿指定边种类指向的所有目标节点。
    /// </summary>
    private static IEnumerable<RoslynCpgNode> GetOutgoingTargets(RuleContext context, NodeId sourceNodeId, RoslynCpgEdgeKind edgeKind)
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

    /// <summary>
    /// 判断图节点是否位于给定方法声明的源码范围内。
    /// </summary>
    private static bool IsInsideMethod(RoslynCpgNode node, MethodDeclarationSyntax methodSyntax)
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

    /// <summary>
    /// 在无法从编译入口恢复时，退化按 Main 名称识别入口方法。
    /// </summary>
    private static bool IsEntryMethod(RoslynCpgNode node)
    {
        return node.Kind == RoslynCpgNodeKind.Method &&
          string.Equals(node.Name, "Main", StringComparison.Ordinal);
    }

    /// <summary>
    /// 根据方法符号的源码位置，在图中定位对应的方法抽象节点。
    /// </summary>
    private static RoslynCpgNode? FindMethodNodeBySymbol(RuleContext context, IMethodSymbol methodSymbol)
    {
        var location = methodSymbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is null || location.SourceTree?.FilePath is not string filePath)
        {
            return null;
        }

        return context.GetGraphNodesByKind(RoslynCpgNodeKind.Method)
          .FirstOrDefault(node =>
            string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
            node.SpanStart == location.SourceSpan.Start &&
            node.SpanEnd == location.SourceSpan.End &&
            string.Equals(node.Name, methodSymbol.Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 为每个方法抽象节点建立到源码方法声明的映射。
    /// </summary>
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

    /// <summary>
    /// 用文件路径和跨度构造稳定的位置键。
    /// </summary>
    private static string BuildLocationKey(string filePath, int spanStart, int spanEnd)
    {
        return $"{filePath}|{spanStart}|{spanEnd}";
    }

    /// <summary>
    /// 基于源码位置，把方法符号节点对齐回方法抽象节点。
    /// </summary>
    private static RoslynCpgNode? ResolveMethodNode(RoslynCpgNode symbolNode, IReadOnlyDictionary<string, RoslynCpgNode> methodByLocation)
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
