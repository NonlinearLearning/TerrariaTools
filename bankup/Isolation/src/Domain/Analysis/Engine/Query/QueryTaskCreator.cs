using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 根据 sink 集合创建查询任务。
///
/// 这个类型对齐 Joern `TaskCreator.scala` 的“任务入口创建”职责，
/// 但当前只保留最小部分：按 sink 一对一建任务。
/// </summary>
public sealed class QueryTaskCreator
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化任务创建器。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public QueryTaskCreator(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 为每个 sink 节点创建一个任务。
    /// </summary>
    /// <param name="sinkNodeIds">sink 节点编号集合。</param>
    /// <returns>查询任务集合。</returns>
    public IReadOnlyList<QueryTask> CreateOneTaskPerSink(IEnumerable<long> sinkNodeIds)
    {
        ArgumentNullException.ThrowIfNull(sinkNodeIds);

        return sinkNodeIds
            .Distinct()
            .Select(sinkNodeId =>
            {
                _ = graph.GetNode(sinkNodeId);
                return new QueryTask(sinkNodeId);
            })
            .ToArray();
    }
}
