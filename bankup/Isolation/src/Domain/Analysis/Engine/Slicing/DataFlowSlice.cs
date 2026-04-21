namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 表示数据流切片的可序列化结果。
/// </summary>
public sealed class DataFlowSlice
{
    /// <summary>
    /// 使用节点和边初始化数据流切片。
    /// </summary>
    /// <param name="nodes">切片节点。</param>
    /// <param name="edges">切片边。</param>
    public DataFlowSlice(IEnumerable<SliceNode> nodes, IEnumerable<SliceEdge> edges)
    {
        Nodes = nodes?.OrderBy(node => node.Id).ToArray()
            ?? throw new ArgumentNullException(nameof(nodes));
        Edges = edges?.OrderBy(edge => edge.SourceId).ThenBy(edge => edge.TargetId).ToArray()
            ?? throw new ArgumentNullException(nameof(edges));
    }

    /// <summary>
    /// 获取切片节点。
    /// </summary>
    public IReadOnlyList<SliceNode> Nodes { get; }

    /// <summary>
    /// 获取切片边。
    /// </summary>
    public IReadOnlyList<SliceEdge> Edges { get; }
}
