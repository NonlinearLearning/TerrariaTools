using Analysis.Core;

namespace Analysis.Slicing;

/// <summary>
/// 表示一次切片操作得到的节点和边集合。
/// </summary>
public sealed class SliceResult
{
    /// <summary>
    /// 使用切片结果初始化对象。
    /// </summary>
    /// <param name="criterionNodeId">切片基准节点编号。</param>
    /// <param name="nodes">命中的节点集合。</param>
    /// <param name="edges">命中的边集合。</param>
    public SliceResult(long criterionNodeId, IEnumerable<CpgNode> nodes, IEnumerable<CpgEdge> edges)
    {
        CriterionNodeId = criterionNodeId;
        Nodes = nodes?.OrderBy(node => node.Id).ToArray()
            ?? throw new ArgumentNullException(nameof(nodes));
        Edges = edges?.OrderBy(edge => edge.SourceId).ThenBy(edge => edge.TargetId).ToArray()
            ?? throw new ArgumentNullException(nameof(edges));
    }

    /// <summary>
    /// 获取切片基准节点编号。
    /// </summary>
    public long CriterionNodeId { get; }

    /// <summary>
    /// 获取切片覆盖的节点集合。
    /// </summary>
    public IReadOnlyList<CpgNode> Nodes { get; }

    /// <summary>
    /// 获取切片覆盖的边集合。
    /// </summary>
    public IReadOnlyList<CpgEdge> Edges { get; }
}
