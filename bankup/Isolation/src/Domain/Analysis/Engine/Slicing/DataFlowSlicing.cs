using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 提供数据流切片入口。
///
/// 这个类型对应 Joern `DataFlowSlicing.scala`：
/// 从一个或多个 sink 出发，沿 `ReachingDef` 反向收集相关节点和边。
/// </summary>
public sealed class DataFlowSlicing
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化切片入口。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public DataFlowSlicing(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 对一组 sink 节点计算数据流切片。
    /// </summary>
    /// <param name="sinkNodeIds">sink 节点集合。</param>
    /// <returns>可序列化的数据流切片。</returns>
    public DataFlowSlice Calculate(IEnumerable<long> sinkNodeIds)
    {
        ArgumentNullException.ThrowIfNull(sinkNodeIds);

        DataFlowSlicer slicer = new(graph);
        List<CpgNode> nodes = new();
        List<CpgEdge> edges = new();

        foreach (long sinkNodeId in sinkNodeIds.Distinct())
        {
            SliceResult result = slicer.Slice(new SliceCriterion(sinkNodeId), SliceDirection.Backward);
            nodes.AddRange(result.Nodes);
            edges.AddRange(result.Edges);
        }

        return new DataFlowSlice(
            nodes.GroupBy(node => node.Id).Select(group => new SliceNode(group.First())),
            edges.GroupBy(edge => (edge.SourceId, edge.TargetId, edge.Kind, edge.Label))
                .Select(group => new SliceEdge(group.First())));
    }
}
