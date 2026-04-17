using Analysis.Core;

namespace Analysis.Query;

/// <summary>
/// Joern `TaskSolver.scala` 的同名包装。
/// </summary>
public sealed class TaskSolver
{
    private readonly QueryTaskSolver innerSolver;

    /// <summary>
    /// 使用目标图初始化任务求解器。
    /// </summary>
    public TaskSolver(CpgGraph graph)
    {
        innerSolver = new QueryTaskSolver(graph);
    }

    /// <summary>
    /// 反向求解一个查询任务。
    /// </summary>
    public IReadOnlyList<DataFlowPath> SolveBackward(
        QueryTask task,
        IReadOnlySet<long> sourceNodeIds,
        int maxDepth)
    {
        return innerSolver.SolveBackward(task, sourceNodeIds, maxDepth);
    }
}
