using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 表示一组节点上的链式查询。
///
/// 当前版本刻意只做最小子集：
/// - 按类型筛选；
/// - 按编号筛选；
/// - 顺着指定边类型走出边或入边；
/// - 最终转成只读列表。
/// </summary>
public sealed class NodeQuery
{
    private readonly CpgGraph graph;
    private readonly IReadOnlyList<CpgNode> nodes;

    /// <summary>
    /// 使用图和当前节点集合初始化查询对象。
    /// </summary>
    /// <param name="graph">所属图。</param>
    /// <param name="nodes">当前候选节点集合。</param>
    public NodeQuery(CpgGraph graph, IEnumerable<CpgNode> nodes)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        ArgumentNullException.ThrowIfNull(nodes);
        this.nodes = nodes.ToArray();
    }

    /// <summary>
    /// 仅保留指定类型的节点。
    /// </summary>
    /// <param name="kind">目标节点类型。</param>
    /// <returns>新的查询对象。</returns>
    public NodeQuery OfKind(CpgNodeKind kind)
    {
        return new NodeQuery(graph, nodes.Where(node => node.Kind == kind));
    }

    /// <summary>
    /// 仅保留指定编号的节点。
    /// </summary>
    /// <param name="nodeId">目标节点编号。</param>
    /// <returns>新的查询对象。</returns>
    public NodeQuery WhereId(long nodeId)
    {
        return new NodeQuery(graph, nodes.Where(node => node.Id == nodeId));
    }

    /// <summary>
    /// 顺着指定边类型访问出边终点。
    /// </summary>
    /// <param name="kind">边类型。</param>
    /// <returns>新的查询对象。</returns>
    public NodeQuery Outgoing(CpgEdgeKind kind)
    {
        return new NodeQuery(
            graph,
            nodes.SelectMany(node => graph.GetOutgoingEdges(node.Id, kind))
                .Select(edge => graph.GetNode(edge.TargetId))
                .DistinctBy(node => node.Id));
    }

    /// <summary>
    /// 顺着指定边类型访问入边起点。
    /// </summary>
    /// <param name="kind">边类型。</param>
    /// <returns>新的查询对象。</returns>
    public NodeQuery Incoming(CpgEdgeKind kind)
    {
        return new NodeQuery(
            graph,
            nodes.SelectMany(node => graph.GetIncomingEdges(node.Id, kind))
                .Select(edge => graph.GetNode(edge.SourceId))
                .DistinctBy(node => node.Id));
    }

    /// <summary>
    /// 将结果物化成只读列表。
    /// </summary>
    /// <returns>当前查询对应的节点列表。</returns>
    public IReadOnlyList<CpgNode> ToList()
    {
        return nodes;
    }
}
