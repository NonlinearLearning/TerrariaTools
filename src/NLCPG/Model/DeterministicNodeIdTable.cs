namespace NLCPG.Model;

/// <summary>
/// 为稳定节点锚点分配可重复生成的连续 <see cref="NodeId"/>。
/// </summary>
public sealed class DeterministicNodeIdTable
{
    private readonly Dictionary<StableNodeAnchor, NodeId> _ids;

    private DeterministicNodeIdTable(Dictionary<StableNodeAnchor, NodeId> ids)
    {
        _ids = ids;
    }

    // 按稳定锚点的确定性排序为节点分配连续 NodeId。
    public static DeterministicNodeIdTable Create(IEnumerable<StableNodeAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        var orderedAnchors = anchors
          .Distinct()
          .OrderBy(anchor => anchor.Kind)
          .ThenBy(anchor => anchor.FilePathId)
          .ThenBy(anchor => anchor.SpanStart)
          .ThenBy(anchor => anchor.SpanEnd)
          .ThenBy(anchor => anchor.Role)
          .ThenBy(anchor => anchor.Ordinal)
          .ThenBy(anchor => anchor.ExtraKeyId)
          .ToArray();
        var ids = new Dictionary<StableNodeAnchor, NodeId>();
        for (var index = 0; index < orderedAnchors.Length; index += 1)
        {
            ids[orderedAnchors[index]] = new NodeId((uint)index + 1);
        }

        return new DeterministicNodeIdTable(ids);
    }

    // 尝试读取给定稳定锚点对应的 NodeId。
    public bool TryGetNodeId(StableNodeAnchor anchor, out NodeId nodeId)
    {
        return _ids.TryGetValue(anchor, out nodeId);
    }

    // 返回给定稳定锚点的 NodeId；缺失时抛出异常。
    public NodeId GetRequiredId(StableNodeAnchor anchor)
    {
        return _ids.TryGetValue(anchor, out var nodeId)
          ? nodeId
          : throw new KeyNotFoundException($"Stable anchor was not preallocated: {anchor}.");
    }

    // 判断当前分配表是否包含给定稳定锚点。
    public bool Contains(StableNodeAnchor anchor)
    {
        return _ids.ContainsKey(anchor);
    }

    /// <summary>已分配节点标识的数量。</summary>
    public int Count => _ids.Count;

    // 暴露当前稳定锚点到 NodeId 的只读视图。
    public IReadOnlyDictionary<StableNodeAnchor, NodeId> Snapshot()
    {
        return _ids;
    }
}
