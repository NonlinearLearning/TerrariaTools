namespace Analysis.Query;

/// <summary>
/// 表示一条数据流路径。
///
/// 当前实现只保存节点编号路径。
/// 后续如果要叠加边标签、源码位置、跨方法信息，可以继续扩展这个类型。
/// </summary>
public sealed class DataFlowPath
{
    /// <summary>
    /// 使用一条节点编号路径初始化结果对象。
    /// </summary>
    /// <param name="nodeIds">路径上的节点编号，顺序为 source 到 sink。</param>
    public DataFlowPath(IEnumerable<long> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        NodeIds = nodeIds.ToArray();
    }

    /// <summary>
    /// 获取路径上的节点编号序列。
    /// </summary>
    public IReadOnlyList<long> NodeIds { get; }
}
