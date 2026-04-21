using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 提供面向 source/sink 集合的数据流查询入口。
///
/// 这个类型对应 Joern `queryengine/Engine.scala` 的本地简化版：
/// - 不做并行任务调度；
/// - 不做跨过程缓存复用；
/// - 先把“从 sources 到 sinks 找路径”这条主链路稳定落下。
/// </summary>
public sealed class QueryEngine
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化查询引擎。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public QueryEngine(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 针对一组 sink 和一组 source 查找全部可达路径。
    /// </summary>
    /// <param name="sinkNodeIds">sink 节点集合。</param>
    /// <param name="sourceNodeIds">source 节点集合。</param>
    /// <param name="maxDepth">最大搜索深度。</param>
    /// <returns>命中的数据流路径。</returns>
    public IReadOnlyList<DataFlowPath> BackwardFromSinks(
        IEnumerable<long> sinkNodeIds,
        IEnumerable<long> sourceNodeIds,
        int maxDepth = 64)
    {
        ArgumentNullException.ThrowIfNull(sinkNodeIds);
        ArgumentNullException.ThrowIfNull(sourceNodeIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);

        HashSet<long> sources = sourceNodeIds.ToHashSet();
        QueryTaskCreator taskCreator = new(graph);
        QueryTaskSolver taskSolver = new(graph);
        List<DataFlowPath> results = new();

        foreach (QueryTask task in taskCreator.CreateOneTaskPerSink(sinkNodeIds))
        {
            results.AddRange(taskSolver.SolveBackward(task, sources, maxDepth));
        }

        return results
            .GroupBy(path => string.Join(",", path.NodeIds))
            .Select(group => group.First())
            .OrderBy(path => path.NodeIds.Count)
            .ThenBy(path => string.Join(",", path.NodeIds), StringComparer.Ordinal)
            .ToArray();
    }
}
