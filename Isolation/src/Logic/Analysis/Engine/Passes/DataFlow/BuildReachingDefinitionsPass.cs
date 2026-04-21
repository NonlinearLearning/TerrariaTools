using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// 为每个方法计算最小可用的 reaching definitions 结果。
///
/// 当前实现只覆盖本项目阶段二之后最需要的几类定义：
/// - 方法入参；
/// - 局部变量声明；
/// - 简单赋值左值。
///
/// 它不追求一次对齐 Joern `ReachingDefPass` 的全部细节，
/// 但会把“某个使用点当前能看到哪些定义”稳定写回节点属性，
/// 让后续 `BuildDdgPass` 可以补出 `ReachingDef` 边。
/// </summary>
public sealed class BuildReachingDefinitionsPass : CpgPass
{
    internal const string IncomingDefinitionsPropertyName = "IncomingDefinitions";


    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode methodNode in builder.Graph.GetNodes(CpgNodeKind.Method))
        {
            ComputeMethod(builder.Graph, methodNode);
        }
    }

    private static void ComputeMethod(CpgGraph graph, CpgNode methodNode)
    {
        List<CpgNode> cfgNodes = GetMethodCfgNodes(graph, methodNode);
        if (cfgNodes.Count == 0)
        {
            return;
        }

        Dictionary<long, List<DataFlowDefinition>> incomingDefinitions = cfgNodes.ToDictionary(
            node => node.Id,
            _ => new List<DataFlowDefinition>());
        Dictionary<long, List<DataFlowDefinition>> outgoingDefinitions = cfgNodes.ToDictionary(
            node => node.Id,
            _ => new List<DataFlowDefinition>());
        IReadOnlyList<DataFlowDefinition> parameterDefinitions = GetParameterDefinitions(graph, methodNode);
        HashSet<long> methodScopeNodeIds = cfgNodes.Select(node => node.Id)
            .Concat(new[] { methodNode.Id })
            .ToHashSet();

        for (int iteration = 0; iteration < 12; iteration++)
        {
            bool changed = false;

            foreach (CpgNode node in cfgNodes.OrderBy(GetOrderKey))
            {
                List<DataFlowDefinition> nextIncoming = new();
                foreach (CpgEdge incomingCfgEdge in graph.GetIncomingEdges(node.Id, CpgEdgeKind.Cfg))
                {
                    if (!methodScopeNodeIds.Contains(incomingCfgEdge.SourceId))
                    {
                        continue;
                    }

                    if (incomingCfgEdge.SourceId == methodNode.Id)
                    {
                        nextIncoming.AddRange(parameterDefinitions);
                        continue;
                    }

                    nextIncoming.AddRange(outgoingDefinitions[incomingCfgEdge.SourceId]);
                }

                nextIncoming = MergeDefinitions(nextIncoming);
                List<DataFlowDefinition> nextOutgoing = ApplyTransfer(node, nextIncoming, graph);

                if (!DefinitionListsEqual(incomingDefinitions[node.Id], nextIncoming))
                {
                    incomingDefinitions[node.Id] = nextIncoming;
                    changed = true;
                }

                if (!DefinitionListsEqual(outgoingDefinitions[node.Id], nextOutgoing))
                {
                    outgoingDefinitions[node.Id] = nextOutgoing;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        foreach (CpgNode node in cfgNodes)
        {
            IReadOnlyCollection<long> usedSymbolIds = GetUsedSymbolNodeIds(graph, node).ToArray();
            if (usedSymbolIds.Count == 0)
            {
                node.SetProperty(IncomingDefinitionsPropertyName, Array.Empty<DataFlowDefinition>());
                continue;
            }

            DataFlowDefinition[] relevantDefinitions = incomingDefinitions[node.Id]
                .Where(definition => usedSymbolIds.Contains(definition.SymbolNodeId))
                .Distinct()
                .ToArray();
            node.SetProperty(IncomingDefinitionsPropertyName, relevantDefinitions);
        }
    }

    private static List<DataFlowDefinition> ApplyTransfer(
        CpgNode node,
        IReadOnlyCollection<DataFlowDefinition> incomingDefinitions,
        CpgGraph graph)
    {
        List<DataFlowDefinition> currentDefinitions = incomingDefinitions.ToList();
        foreach (DataFlowDefinition generatedDefinition in GetGeneratedDefinitions(graph, node))
        {
            currentDefinitions.RemoveAll(definition => definition.SymbolNodeId == generatedDefinition.SymbolNodeId);
            currentDefinitions.Add(generatedDefinition);
        }

        return MergeDefinitions(currentDefinitions);
    }

    private static IReadOnlyList<DataFlowDefinition> GetParameterDefinitions(CpgGraph graph, CpgNode methodNode)
    {
        return graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .Where(node =>
                node.TryGetProperty<long>("AstParentId", out long parentId) &&
                parentId == methodNode.Id &&
                node.TryGetProperty<string>("Name", out string? name) &&
                !string.IsNullOrWhiteSpace(name))
            .OrderBy(GetOrderKey)
            .Select(node => new DataFlowDefinition(node.Id, node.Id, GetNodeName(node)))
            .ToArray();
    }

    private static List<CpgNode> GetMethodCfgNodes(CpgGraph graph, CpgNode methodNode)
    {
        HashSet<long> descendantIds = GetMethodDescendantIds(graph, methodNode);
        return graph.Nodes
            .Where(node => descendantIds.Contains(node.Id))
            .Where(IsCfgRelevantNode)
            .ToList();
    }

    private static HashSet<long> GetMethodDescendantIds(CpgGraph graph, CpgNode methodNode)
    {
        HashSet<long> visited = new();
        Queue<long> queue = new();
        foreach (CpgEdge astEdge in graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Ast))
        {
            queue.Enqueue(astEdge.TargetId);
        }

        while (queue.Count > 0)
        {
            long currentId = queue.Dequeue();
            if (!visited.Add(currentId))
            {
                continue;
            }

            foreach (CpgEdge astEdge in graph.GetOutgoingEdges(currentId, CpgEdgeKind.Ast))
            {
                queue.Enqueue(astEdge.TargetId);
            }
        }

        return visited;
    }

    private static bool IsCfgRelevantNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.Local
            or CpgNodeKind.Identifier
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.MethodReturn;
    }

    private static IEnumerable<DataFlowDefinition> GetGeneratedDefinitions(CpgGraph graph, CpgNode node)
    {
        if (node.Kind == CpgNodeKind.Local &&
            node.TryGetProperty<string>("Name", out string? localName) &&
            !string.IsNullOrWhiteSpace(localName))
        {
            yield return new DataFlowDefinition(node.Id, node.Id, localName);
            yield break;
        }

        if (node.Kind != CpgNodeKind.Call ||
            !node.TryGetProperty<string>("Name", out string? callName) ||
            !string.Equals(callName, "=", StringComparison.Ordinal))
        {
            yield break;
        }

        CpgNode? leftNode = graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .FirstOrDefault();
        if (leftNode is null)
        {
            yield break;
        }

        CpgNode? targetNode = ResolveDefinitionTarget(graph, leftNode);
        if (targetNode is null)
        {
            yield break;
        }

        string label = GetNodeName(targetNode);
        if (string.IsNullOrWhiteSpace(label))
        {
            yield break;
        }

        yield return new DataFlowDefinition(targetNode.Id, targetNode.Id, label);
    }

    private static CpgNode? ResolveDefinitionTarget(CpgGraph graph, CpgNode leftNode)
    {
        if (leftNode.Kind is CpgNodeKind.Local or CpgNodeKind.MethodParameterIn or CpgNodeKind.Member)
        {
            return leftNode;
        }

        return graph.GetOutgoingEdges(leftNode.Id, CpgEdgeKind.Ref)
            .Select(edge => graph.GetNode(edge.TargetId))
            .FirstOrDefault(target =>
                target.Kind is CpgNodeKind.Local or CpgNodeKind.MethodParameterIn or CpgNodeKind.Member);
    }

    private static IEnumerable<long> GetUsedSymbolNodeIds(CpgGraph graph, CpgNode node)
    {
        HashSet<long> excludedNodeIds = GetExcludedDefinitionNodeIds(graph, node);

        foreach (CpgNode descendant in EnumerateAstSubtree(graph, node))
        {
            if (excludedNodeIds.Contains(descendant.Id) || descendant.Kind != CpgNodeKind.Identifier)
            {
                continue;
            }

            CpgNode? targetNode = graph.GetOutgoingEdges(descendant.Id, CpgEdgeKind.Ref)
                .Select(edge => graph.GetNode(edge.TargetId))
                .FirstOrDefault(target =>
                    target.Kind is CpgNodeKind.Local or CpgNodeKind.MethodParameterIn or CpgNodeKind.Member);
            if (targetNode is not null)
            {
                yield return targetNode.Id;
            }
        }
    }

    private static HashSet<long> GetExcludedDefinitionNodeIds(CpgGraph graph, CpgNode node)
    {
        HashSet<long> excludedNodeIds = new();
        if (node.Kind != CpgNodeKind.Call ||
            !node.TryGetProperty<string>("Name", out string? callName) ||
            !string.Equals(callName, "=", StringComparison.Ordinal))
        {
            return excludedNodeIds;
        }

        CpgNode? leftNode = graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .FirstOrDefault();
        if (leftNode is null)
        {
            return excludedNodeIds;
        }

        foreach (CpgNode excludedNode in EnumerateAstSubtree(graph, leftNode))
        {
            _ = excludedNodeIds.Add(excludedNode.Id);
        }

        _ = excludedNodeIds.Add(leftNode.Id);
        return excludedNodeIds;
    }

    private static IEnumerable<CpgNode> EnumerateAstSubtree(CpgGraph graph, CpgNode rootNode)
    {
        Stack<CpgNode> stack = new([rootNode]);
        HashSet<long> visited = new();

        while (stack.Count > 0)
        {
            CpgNode current = stack.Pop();
            if (!visited.Add(current.Id))
            {
                continue;
            }

            yield return current;

            foreach (CpgNode child in graph.GetOutgoingEdges(current.Id, CpgEdgeKind.Ast)
                         .Select(edge => graph.GetNode(edge.TargetId))
                         .Reverse())
            {
                stack.Push(child);
            }
        }
    }

    private static List<DataFlowDefinition> MergeDefinitions(IEnumerable<DataFlowDefinition> definitions)
    {
        return definitions
            .GroupBy(definition => (definition.SymbolNodeId, definition.SourceNodeId, definition.Label))
            .Select(group => group.First())
            .OrderBy(definition => definition.SymbolNodeId)
            .ThenBy(definition => definition.SourceNodeId)
            .ThenBy(definition => definition.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static bool DefinitionListsEqual(
        IReadOnlyList<DataFlowDefinition> left,
        IReadOnlyList<DataFlowDefinition> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetNodeName(CpgNode node)
    {
        return node.TryGetProperty<string>("Name", out string? name) ? name ?? string.Empty : string.Empty;
    }

    private static (int Line, int Column, long Id) GetOrderKey(CpgNode node)
    {
        int line = node.TryGetProperty<int>("Line", out int actualLine) ? actualLine : int.MaxValue;
        int column = node.TryGetProperty<int>("Column", out int actualColumn) ? actualColumn : int.MaxValue;
        return (line, column, node.Id);
    }
}

/// <summary>
/// 表示一条“某变量由某节点定义”的最小 reaching definition 事实。
/// </summary>
public sealed record DataFlowDefinition(long SymbolNodeId, long SourceNodeId, string Label);
