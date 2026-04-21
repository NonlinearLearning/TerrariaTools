using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 提供最小数据流路径查询能力。
///
/// 当前版本只基于 `ReachingDef` 边做路径搜索，
/// 目标是先让图上的数据流结果可以被查询和解释。
/// </summary>
public sealed class DataFlowQuery
{
    private readonly CpgGraph graph;
    private long? sourceNodeId;
    private long? sinkNodeId;
    private int maxDepth = 64;

    /// <summary>
    /// 使用目标图初始化数据流查询对象。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public DataFlowQuery(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 指定数据流起点。
    /// </summary>
    /// <param name="nodeId">起点节点编号。</param>
    /// <returns>当前查询对象。</returns>
    public DataFlowQuery From(long nodeId)
    {
        sourceNodeId = nodeId;
        return this;
    }

    /// <summary>
    /// 指定数据流终点。
    /// </summary>
    /// <param name="nodeId">终点节点编号。</param>
    /// <returns>当前查询对象。</returns>
    public DataFlowQuery To(long nodeId)
    {
        sinkNodeId = nodeId;
        return this;
    }

    /// <summary>
    /// 设置最大搜索深度。
    /// </summary>
    /// <param name="depth">最大深度，必须大于零。</param>
    /// <returns>当前查询对象。</returns>
    public DataFlowQuery WithMaxDepth(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        maxDepth = depth;
        return this;
    }

    /// <summary>
    /// 查找从 source 到 sink 的所有最小可达路径。
    /// </summary>
    /// <returns>找到的路径集合。</returns>
    public IReadOnlyList<DataFlowPath> FindPaths()
    {
        if (!sourceNodeId.HasValue)
        {
            throw new InvalidOperationException("必须先指定数据流起点。");
        }

        if (!sinkNodeId.HasValue)
        {
            throw new InvalidOperationException("必须先指定数据流终点。");
        }

        if (sourceNodeId.Value == sinkNodeId.Value)
        {
            return new[] { new DataFlowPath(new[] { sourceNodeId.Value }) };
        }

        List<DataFlowPath> results = new();
        Queue<(long NodeId, List<long> Path)> queue = new();
        queue.Enqueue((sourceNodeId.Value, new List<long> { sourceNodeId.Value }));

        while (queue.Count > 0)
        {
            (long currentNodeId, List<long> path) = queue.Dequeue();
            if (path.Count > maxDepth)
            {
                continue;
            }

            foreach (CpgEdge edge in GetOutgoingDataFlowEdges(currentNodeId))
            {
                if (path.Contains(edge.TargetId))
                {
                    continue;
                }

                List<long> nextPath = new(path) { edge.TargetId };
                if (edge.TargetId == sinkNodeId.Value)
                {
                    results.Add(new DataFlowPath(nextPath));
                    continue;
                }

                queue.Enqueue((edge.TargetId, nextPath));
            }
        }

        return results
            .OrderBy(path => path.NodeIds.Count)
            .ThenBy(path => string.Join(",", path.NodeIds), StringComparer.Ordinal)
            .ToArray();
    }

    private IEnumerable<CpgEdge> GetOutgoingDataFlowEdges(long currentNodeId)
    {
        return graph.GetOutgoingEdges(currentNodeId, CpgEdgeKind.ReachingDef)
            .Concat(graph.GetOutgoingEdges(currentNodeId, CpgEdgeKind.ParameterLink));
    }
}
