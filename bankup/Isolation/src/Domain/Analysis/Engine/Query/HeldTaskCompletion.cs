namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 管理已经启动但需要等待缓存结果的查询任务。
///
/// 对应 Joern `HeldTaskCompletion.scala`。Joern 用它补齐并行查询中被暂挂的任务；
/// 当前 C# 版先保留确定性的单线程结果合并能力。
/// </summary>
public sealed class HeldTaskCompletion
{
    private readonly List<QueryTask> heldTasks = new();
    private readonly Dictionary<QueryTask, List<DataFlowPath>> resultTable = new();

    /// <summary>
    /// 获取当前仍被挂起的任务。
    /// </summary>
    public IReadOnlyList<QueryTask> HeldTasks => heldTasks;

    /// <summary>
    /// 暂挂一个任务。
    /// </summary>
    public void Hold(QueryTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!heldTasks.Contains(task))
        {
            heldTasks.Add(task);
        }
    }

    /// <summary>
    /// 给任务登记已经求出的路径。
    /// </summary>
    public void AddResult(QueryTask task, DataFlowPath path)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(path);

        if (!resultTable.TryGetValue(task, out List<DataFlowPath>? paths))
        {
            paths = new List<DataFlowPath>();
            resultTable[task] = paths;
        }

        paths.Add(path);
    }

    /// <summary>
    /// 完成所有已经具备结果的挂起任务。
    /// </summary>
    public IReadOnlyList<DataFlowPath> CompleteHeldTasks()
    {
        List<DataFlowPath> completed = new();
        foreach (QueryTask task in heldTasks.ToArray())
        {
            if (!resultTable.TryGetValue(task, out List<DataFlowPath>? paths))
            {
                continue;
            }

            completed.AddRange(paths);
            heldTasks.Remove(task);
        }

        return completed
            .GroupBy(path => string.Join(",", path.NodeIds))
            .Select(group => group.First())
            .ToArray();
    }
}
