using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// Joern `TaskCreator.scala` 的同名包装。
/// </summary>
public sealed class TaskCreator
{
    private readonly QueryTaskCreator innerCreator;

    /// <summary>
    /// 使用目标图初始化任务创建器。
    /// </summary>
    public TaskCreator(CpgGraph graph)
    {
        innerCreator = new QueryTaskCreator(graph);
    }

    /// <summary>
    /// 为每个 sink 创建一个任务。
    /// </summary>
    public IReadOnlyList<QueryTask> CreateOneTaskPerSink(IEnumerable<long> sinkNodeIds)
    {
        return innerCreator.CreateOneTaskPerSink(sinkNodeIds);
    }
}
