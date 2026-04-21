namespace Logic.Analysis.Engine.Passes.ControlFlow;

/// <summary>
/// 表示一个局部控制流片段。
///
/// 每个语句先返回自己的入口、出口和边，再由上层语句块拼起来。
/// 这样处理分支、循环和提前返回时，不需要在一个大方法里硬凑所有情况。
/// </summary>
public sealed class CfgModel
{
    /// <summary>
    /// 获取或设置片段入口节点。
    /// </summary>
    public long? EntryNodeId { get; set; }

    /// <summary>
    /// 获取片段出口节点集合。
    /// </summary>
    public HashSet<long> ExitNodeIds { get; } = new();

    /// <summary>
    /// 获取片段内部已经确定的边。
    /// </summary>
    public List<(long SourceId, long TargetId)> Edges { get; } = new();

    /// <summary>
    /// 获取尚未回填目标的 break 节点。
    /// </summary>
    public HashSet<long> PendingBreaks { get; } = new();

    /// <summary>
    /// 获取尚未回填目标的 continue 节点。
    /// </summary>
    public HashSet<long> PendingContinues { get; } = new();

    /// <summary>
    /// 获取已经终止方法路径的 return 节点。
    /// </summary>
    public HashSet<long> PendingReturns { get; } = new();
}
