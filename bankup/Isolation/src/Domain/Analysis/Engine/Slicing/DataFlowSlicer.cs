using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 基于 `ReachingDef` 边执行最小前向和后向切片。
///
/// 当前只依赖已经生成好的数据流边，
/// 不重复求解 reaching definition，也不在这里做跨过程扩展。
/// </summary>
public sealed class DataFlowSlicer
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化切片器。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public DataFlowSlicer(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 按指定方向执行切片。
    /// </summary>
    /// <param name="criterion">切片基准条件。</param>
    /// <param name="direction">切片方向。</param>
    /// <returns>切片结果。</returns>
    public SliceResult Slice(SliceCriterion criterion, SliceDirection direction)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        CpgNode criterionNode = graph.GetNode(criterion.NodeId);
        HashSet<long> visitedNodeIds = new() { criterionNode.Id };
        List<CpgEdge> visitedEdges = new();
        Queue<long> queue = new();
        queue.Enqueue(criterionNode.Id);

        while (queue.Count > 0)
        {
            long currentNodeId = queue.Dequeue();
            IEnumerable<CpgEdge> nextEdges = direction == SliceDirection.Forward
                ? graph.GetOutgoingEdges(currentNodeId, CpgEdgeKind.ReachingDef)
                : graph.GetIncomingEdges(currentNodeId, CpgEdgeKind.ReachingDef);

            foreach (CpgEdge edge in nextEdges)
            {
                visitedEdges.Add(edge);
                long nextNodeId = direction == SliceDirection.Forward ? edge.TargetId : edge.SourceId;
                if (visitedNodeIds.Add(nextNodeId))
                {
                    queue.Enqueue(nextNodeId);
                }
            }
        }

        IReadOnlyList<CpgNode> nodes = visitedNodeIds
            .Select(graph.GetNode)
            .OrderBy(node => node.Id)
            .ToArray();
        return new SliceResult(criterion.NodeId, nodes, visitedEdges.Distinct().ToArray());
    }
}
