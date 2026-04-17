using Analysis.Core;

namespace Analysis.Passes.DataFlow;

/// <summary>
/// 为单个方法创建 reaching definition 问题。
///
/// 对应 Joern `ReachingDefProblem.scala`。它把方法、参数、方法体 CFG 节点组织成一张
/// 数据流求解图，并给每个会产生定义的节点计算 GEN 集。
/// </summary>
public sealed class ReachingDefinitionProblem
{
    private ReachingDefinitionProblem(
        ReachingDefinitionFlowGraph flowGraph,
        ReachingDefinitionTransferFunction transferFunction,
        DataFlowProblem<CpgNode, IReadOnlySet<DataFlowDefinition>> problem)
    {
        FlowGraph = flowGraph;
        TransferFunction = transferFunction;
        Problem = problem;
    }

    /// <summary>
    /// 获取适配后的方法流图。
    /// </summary>
    public ReachingDefinitionFlowGraph FlowGraph { get; }

    /// <summary>
    /// 获取 reaching definition 传递函数。
    /// </summary>
    public ReachingDefinitionTransferFunction TransferFunction { get; }

    /// <summary>
    /// 获取通用数据流问题。
    /// </summary>
    public DataFlowProblem<CpgNode, IReadOnlySet<DataFlowDefinition>> Problem { get; }

    /// <summary>
    /// 获取每个节点生成的定义集合。
    /// </summary>
    public IReadOnlyDictionary<CpgNode, IReadOnlySet<DataFlowDefinition>> GeneratedDefinitions =>
        TransferFunction.GeneratedDefinitions;

    /// <summary>
    /// 基于图和方法节点创建 reaching definition 问题。
    /// </summary>
    public static ReachingDefinitionProblem Create(CpgGraph graph, CpgNode methodNode)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(methodNode);

        ReachingDefinitionFlowGraph flowGraph = new(graph, methodNode);
        ReachingDefinitionTransferFunction transfer = new(flowGraph);
        ReachingDefinitionInit init = new(flowGraph.AllNodesReversePostOrder, transfer.GeneratedDefinitions);
        DataFlowProblem<CpgNode, IReadOnlySet<DataFlowDefinition>> problem = DataFlowProblem<CpgNode, IReadOnlySet<DataFlowDefinition>>.Forward(
            flowGraph,
            transfer,
            (left, right) => left.Concat(right).ToHashSet(),
            init,
            new HashSet<DataFlowDefinition>());

        return new ReachingDefinitionProblem(flowGraph, transfer, problem);
    }
}

/// <summary>
/// 把 CPG 方法节点包装成数据流求解器需要的流图。
/// </summary>
public sealed class ReachingDefinitionFlowGraph : IFlowGraph<CpgNode>
{
    private readonly CpgGraph graph;
    private readonly Dictionary<long, CpgNode> nodesById;

    /// <summary>
    /// 使用目标方法初始化流图。
    /// </summary>
    public ReachingDefinitionFlowGraph(CpgGraph graph, CpgNode methodNode)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        MethodNode = methodNode ?? throw new ArgumentNullException(nameof(methodNode));
        AllNodesReversePostOrder = BuildNodes().ToArray();
        AllNodesPostOrder = AllNodesReversePostOrder.Reverse().ToArray();
        nodesById = AllNodesReversePostOrder.ToDictionary(node => node.Id);
    }

    /// <summary>
    /// 获取被分析的方法节点。
    /// </summary>
    public CpgNode MethodNode { get; }

    /// <inheritdoc />
    public IReadOnlyList<CpgNode> AllNodesReversePostOrder { get; }

    /// <inheritdoc />
    public IReadOnlyList<CpgNode> AllNodesPostOrder { get; }

    /// <inheritdoc />
    public IEnumerable<CpgNode> Successors(CpgNode node)
    {
        if (node.Id == MethodNode.Id)
        {
            foreach (CpgNode parameter in Parameters())
            {
                yield return parameter;
            }

            if (!Parameters().Any())
            {
                foreach (CpgNode first in CfgSuccessors(node))
                {
                    yield return first;
                }
            }

            yield break;
        }

        CpgNode? nextParameter = NextParameter(node);
        if (nextParameter is not null)
        {
            yield return nextParameter;
            yield break;
        }

        foreach (CpgNode successor in CfgSuccessors(node))
        {
            yield return successor;
        }
    }

    /// <inheritdoc />
    public IEnumerable<CpgNode> Predecessors(CpgNode node)
    {
        CpgNode? previousParameter = PreviousParameter(node);
        if (previousParameter is not null)
        {
            yield return previousParameter;
            yield break;
        }

        if (IsFirstParameter(node))
        {
            yield return MethodNode;
            yield break;
        }

        foreach (CpgEdge edge in graph.GetIncomingEdges(node.Id, CpgEdgeKind.Cfg))
        {
            if (nodesById.TryGetValue(edge.SourceId, out CpgNode? predecessor))
            {
                yield return predecessor;
            }
        }
    }

    private IEnumerable<CpgNode> BuildNodes()
    {
        yield return MethodNode;
        foreach (CpgNode parameter in Parameters())
        {
            yield return parameter;
        }

        foreach (CpgNode cfgNode in MethodDescendants().Where(IsCfgRelevantNode).OrderBy(GetOrderKey))
        {
            yield return cfgNode;
        }

        CpgNode? methodReturn = MethodDescendants().FirstOrDefault(node => node.Kind == CpgNodeKind.MethodReturn);
        if (methodReturn is not null && !IsKnown(methodReturn.Id))
        {
            yield return methodReturn;
        }
    }

    private IEnumerable<CpgNode> Parameters()
    {
        return graph.GetNodes(CpgNodeKind.MethodParameterIn)
            .Where(node => node.TryGetProperty<long>("AstParentId", out long parentId) && parentId == MethodNode.Id)
            .OrderBy(GetOrderKey);
    }

    private IEnumerable<CpgNode> MethodDescendants()
    {
        Queue<long> queue = new(graph.GetOutgoingEdges(MethodNode.Id, CpgEdgeKind.Ast).Select(edge => edge.TargetId));
        HashSet<long> visited = new();
        while (queue.Count > 0)
        {
            long currentId = queue.Dequeue();
            if (!visited.Add(currentId))
            {
                continue;
            }

            CpgNode currentNode = graph.GetNode(currentId);
            yield return currentNode;
            foreach (CpgEdge edge in graph.GetOutgoingEdges(currentId, CpgEdgeKind.Ast))
            {
                queue.Enqueue(edge.TargetId);
            }
        }
    }

    private IEnumerable<CpgNode> CfgSuccessors(CpgNode node)
    {
        foreach (CpgEdge edge in graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Cfg))
        {
            if (nodesById.TryGetValue(edge.TargetId, out CpgNode? successor))
            {
                yield return successor;
            }
        }
    }

    private CpgNode? NextParameter(CpgNode node)
    {
        CpgNode[] parameters = Parameters().ToArray();
        int index = Array.FindIndex(parameters, parameter => parameter.Id == node.Id);
        return index >= 0 && index + 1 < parameters.Length ? parameters[index + 1] : null;
    }

    private CpgNode? PreviousParameter(CpgNode node)
    {
        CpgNode[] parameters = Parameters().ToArray();
        int index = Array.FindIndex(parameters, parameter => parameter.Id == node.Id);
        return index > 0 ? parameters[index - 1] : null;
    }

    private bool IsFirstParameter(CpgNode node)
    {
        return Parameters().FirstOrDefault()?.Id == node.Id;
    }

    private bool IsKnown(long nodeId)
    {
        return AllNodesReversePostOrder.Any(node => node.Id == nodeId);
    }

    private static bool IsCfgRelevantNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.Local
            or CpgNodeKind.Identifier
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.MethodReturn;
    }

    private static (int Line, int Column, long Id) GetOrderKey(CpgNode node)
    {
        int line = node.TryGetProperty<int>("Line", out int actualLine) ? actualLine : int.MaxValue;
        int column = node.TryGetProperty<int>("Column", out int actualColumn) ? actualColumn : int.MaxValue;
        return (line, column, node.Id);
    }
}

/// <summary>
/// 实现 reaching definition 的 GEN/KILL 传递函数。
/// </summary>
public sealed class ReachingDefinitionTransferFunction : ITransferFunction<CpgNode, IReadOnlySet<DataFlowDefinition>>
{
    /// <summary>
    /// 使用方法流图初始化传递函数。
    /// </summary>
    public ReachingDefinitionTransferFunction(ReachingDefinitionFlowGraph flowGraph)
    {
        FlowGraph = flowGraph ?? throw new ArgumentNullException(nameof(flowGraph));
        GeneratedDefinitions = FlowGraph.AllNodesReversePostOrder.ToDictionary(
            node => node,
            node => (IReadOnlySet<DataFlowDefinition>)GenerateDefinitions(node).ToHashSet());
    }

    /// <summary>
    /// 获取所属流图。
    /// </summary>
    public ReachingDefinitionFlowGraph FlowGraph { get; }

    /// <summary>
    /// 获取每个节点生成的定义。
    /// </summary>
    public IReadOnlyDictionary<CpgNode, IReadOnlySet<DataFlowDefinition>> GeneratedDefinitions { get; }

    /// <inheritdoc />
    public IReadOnlySet<DataFlowDefinition> Apply(CpgNode node, IReadOnlySet<DataFlowDefinition> value)
    {
        IReadOnlySet<DataFlowDefinition> generated = GeneratedDefinitions[node];
        HashSet<long> killedSymbols = generated.Select(definition => definition.SymbolNodeId).ToHashSet();
        return value.Where(definition => !killedSymbols.Contains(definition.SymbolNodeId))
            .Concat(generated)
            .ToHashSet();
    }

    private static IEnumerable<DataFlowDefinition> GenerateDefinitions(CpgNode node)
    {
        if (!node.TryGetProperty<string>("Name", out string? name) || string.IsNullOrWhiteSpace(name))
        {
            yield break;
        }

        if (node.Kind is CpgNodeKind.MethodParameterIn or CpgNodeKind.Local or CpgNodeKind.Member)
        {
            yield return new DataFlowDefinition(node.Id, node.Id, name);
        }

        if (node.Kind == CpgNodeKind.Call && IsAssignment(node))
        {
            yield return new DataFlowDefinition(node.Id, node.Id, name);
        }
    }

    private static bool IsAssignment(CpgNode node)
    {
        return node.TryGetProperty<string>("Name", out string? name) &&
               string.Equals(name, "=", StringComparison.Ordinal);
    }
}

/// <summary>
/// 为 reaching definition 求解提供初始 IN/OUT 表。
/// </summary>
public sealed class ReachingDefinitionInit : IInOutInit<CpgNode, IReadOnlySet<DataFlowDefinition>>
{
    /// <summary>
    /// 使用节点集合和 GEN 表初始化 IN/OUT。
    /// </summary>
    public ReachingDefinitionInit(
        IEnumerable<CpgNode> nodes,
        IReadOnlyDictionary<CpgNode, IReadOnlySet<DataFlowDefinition>> generatedDefinitions)
    {
        CpgNode[] nodeArray = nodes.ToArray();
        InitIn = nodeArray.ToDictionary(node => node, _ => (IReadOnlySet<DataFlowDefinition>)new HashSet<DataFlowDefinition>());
        InitOut = nodeArray.ToDictionary(
            node => node,
            node => generatedDefinitions.TryGetValue(node, out IReadOnlySet<DataFlowDefinition>? generated)
                ? generated
                : new HashSet<DataFlowDefinition>());
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<CpgNode, IReadOnlySet<DataFlowDefinition>> InitIn { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<CpgNode, IReadOnlySet<DataFlowDefinition>> InitOut { get; }
}
