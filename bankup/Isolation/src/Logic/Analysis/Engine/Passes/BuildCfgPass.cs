using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 根据节点上预先计算出的后继关系补齐最小 CFG。
///
/// 这个 pass 对应 Joern 的 `CfgCreationPass` 的最小版本。
/// 当前实现的目标不是立即复刻完整控制流算法，而是先把控制流边
/// 统一落到图中，让阶段二具备“执行顺序可表达”的基础。
///
/// 约定如下：
/// - 节点若带有 `NextCfgNodeIds` 属性；
/// - 该属性是后继节点编号集合；
/// - pass 负责将这些事实转换成 `Cfg` 边。
/// </summary>
public sealed class BuildCfgPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode source in builder.Graph.Nodes)
        {
            if (!source.TryGetProperty<IReadOnlyCollection<long>>("NextCfgNodeIds", out IReadOnlyCollection<long>? nextIds))
            {
                continue;
            }

            foreach (long nextId in nextIds)
            {
                bool targetExists;
                try
                {
                    builder.Graph.GetNode(nextId);
                    targetExists = true;
                }
                catch (InvalidOperationException)
                {
                    targetExists = false;
                }

                if (!targetExists)
                {
                    continue;
                }

                bool relationExists = builder.Graph
                    .GetOutgoingEdges(source.Id, CpgEdgeKind.Cfg)
                    .Any(edge => edge.TargetId == nextId);

                if (!relationExists)
                {
                    builder.AddEdge(source.Id, nextId, CpgEdgeKind.Cfg);
                }
            }
        }

        foreach (CpgNode methodNode in builder.Graph.GetNodes(CpgNodeKind.Method).ToArray())
        {
            ConnectMethodEntry(builder, methodNode);
            ConnectMethodExits(builder, methodNode);
        }
    }

    private static void ConnectMethodEntry(CpgGraphBuilder builder, CpgNode methodNode)
    {
        IReadOnlyCollection<CpgNode> descendants = GetMethodDescendants(builder.Graph, methodNode);
        CpgNode? entryNode = descendants
            .Where(IsCfgRelevantNode)
            .Where(node => !builder.Graph
                .GetIncomingEdges(node.Id, CpgEdgeKind.Cfg)
                .Any(edge => descendants.Any(candidate => candidate.Id == edge.SourceId)))
            .OrderBy(GetOrderKey)
            .FirstOrDefault();

        if (entryNode is null)
        {
            return;
        }

        if (!builder.Graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Cfg).Any(edge => edge.TargetId == entryNode.Id))
        {
            builder.AddEdge(methodNode.Id, entryNode.Id, CpgEdgeKind.Cfg);
        }
    }

    private static void ConnectMethodExits(CpgGraphBuilder builder, CpgNode methodNode)
    {
        CpgNode? methodReturnNode = builder.Graph.GetNodes(CpgNodeKind.MethodReturn)
            .FirstOrDefault(node =>
                node.TryGetProperty<long>("AstParentId", out long parentId) &&
                parentId == methodNode.Id);
        if (methodReturnNode is null)
        {
            return;
        }

        IReadOnlyCollection<CpgNode> descendants = GetMethodDescendants(builder.Graph, methodNode);
        HashSet<long> descendantIds = descendants.Select(node => node.Id).ToHashSet();

        IEnumerable<CpgNode> exitNodes = descendants
            .Where(IsCfgRelevantNode)
            .Where(node => !IsThrowNode(node))
            .Where(node => !builder.Graph
                .GetOutgoingEdges(node.Id, CpgEdgeKind.Cfg)
                .Any(edge => descendantIds.Contains(edge.TargetId)));

        foreach (CpgNode exitNode in exitNodes)
        {
            if (exitNode.Id == methodReturnNode.Id)
            {
                continue;
            }

            if (builder.Graph.GetOutgoingEdges(exitNode.Id, CpgEdgeKind.Cfg).Any(edge => edge.TargetId == methodReturnNode.Id))
            {
                continue;
            }

            builder.AddEdge(exitNode.Id, methodReturnNode.Id, CpgEdgeKind.Cfg);
        }
    }

    private static IReadOnlyCollection<CpgNode> GetMethodDescendants(CpgGraph graph, CpgNode methodNode)
    {
        List<CpgNode> descendants = new();
        Queue<long> workQueue = new();
        HashSet<long> visited = new();

        foreach (CpgEdge astEdge in graph.GetOutgoingEdges(methodNode.Id, CpgEdgeKind.Ast))
        {
            workQueue.Enqueue(astEdge.TargetId);
        }

        while (workQueue.Count > 0)
        {
            long currentId = workQueue.Dequeue();
            if (!visited.Add(currentId))
            {
                continue;
            }

            CpgNode currentNode = graph.GetNode(currentId);
            descendants.Add(currentNode);
            foreach (CpgEdge astEdge in graph.GetOutgoingEdges(currentId, CpgEdgeKind.Ast))
            {
                workQueue.Enqueue(astEdge.TargetId);
            }
        }

        return descendants;
    }

    private static bool IsCfgRelevantNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.Local
            or CpgNodeKind.Identifier
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.MethodReturn;
    }

    private static bool IsThrowNode(CpgNode node)
    {
        return node.Kind == CpgNodeKind.ControlStructure &&
               node.TryGetProperty<string>("ControlStructureType", out string? controlType) &&
               string.Equals(controlType, "THROW", StringComparison.Ordinal);
    }

    private static (int Line, int Column, long Id) GetOrderKey(CpgNode node)
    {
        int line = node.TryGetProperty<int>("Line", out int actualLine) ? actualLine : int.MaxValue;
        int column = node.TryGetProperty<int>("Column", out int actualColumn) ? actualColumn : int.MaxValue;
        return (line, column, node.Id);
    }
}
