using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 负责执行单个查询任务。
///
/// 这是 Query task 轨的 canonical core：
/// - 从 sink 沿 ReachingDef 反向遍历；
/// - 在命中 source 时产出路径；
/// - 不做部分任务拆分，只返回完整命中路径。
/// </summary>
public sealed class QueryTaskSolver
{
    private readonly CpgGraph _graph;

    /// <summary>
    /// 使用目标图初始化任务求解器。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public QueryTaskSolver(CpgGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 反向求解单个 sink 到 sources 的路径。
    /// </summary>
    /// <param name="task">当前任务。</param>
    /// <param name="sourceNodeIds">合法 source 集合。</param>
    /// <param name="maxDepth">最大深度。</param>
    /// <returns>命中的路径集合。</returns>
    public IReadOnlyList<DataFlowPath> SolveBackward(
        QueryTask task,
        IReadOnlySet<long> sourceNodeIds,
        int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(sourceNodeIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);

        List<DataFlowPath> results = new();
        Queue<List<long>> queue = new();
        queue.Enqueue(new List<long> { task.SinkNodeId });

        while (queue.Count > 0)
        {
            List<long> currentPath = queue.Dequeue();
            long currentNodeId = currentPath[0];

            if (currentPath.Count > maxDepth)
            {
                continue;
            }

            if (sourceNodeIds.Contains(currentNodeId))
            {
                results.Add(new DataFlowPath(currentPath));
                continue;
            }

            foreach (CpgEdge incomingEdge in GetIncomingDataFlowEdges(currentNodeId))
            {
                if (currentPath.Contains(incomingEdge.SourceId))
                {
                    continue;
                }

                List<long> nextPath = new(currentPath.Count + 1) { incomingEdge.SourceId };
                nextPath.AddRange(currentPath);
                queue.Enqueue(nextPath);
            }
        }

        return results;
    }

    private IEnumerable<CpgEdge> GetIncomingDataFlowEdges(long currentNodeId)
    {
        return _graph.GetIncomingEdges(currentNodeId, CpgEdgeKind.ReachingDef)
            .Concat(_graph.GetIncomingEdges(currentNodeId, CpgEdgeKind.ParameterLink));
    }
}
