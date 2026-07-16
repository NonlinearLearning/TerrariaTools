namespace MinimalRoslynCpg.Model;

public sealed class DeterministicNodeIdTable
{
  private readonly Dictionary<StableNodeAnchor, NodeId> _ids;

  private DeterministicNodeIdTable(Dictionary<StableNodeAnchor, NodeId> ids)
  {
    _ids = ids;
  }

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

  public bool TryGetNodeId(StableNodeAnchor anchor, out NodeId nodeId)
  {
    return _ids.TryGetValue(anchor, out nodeId);
  }

  public int Count => _ids.Count;

  public IReadOnlyDictionary<StableNodeAnchor, NodeId> Snapshot()
  {
    return _ids;
  }
}
