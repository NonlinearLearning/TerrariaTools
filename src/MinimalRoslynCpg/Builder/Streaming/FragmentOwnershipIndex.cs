using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Selects one fragment owner for each source span without consulting graph state.
/// </summary>
internal sealed class FragmentOwnershipIndex
{
  private readonly IReadOnlyList<CpgFragmentOwnership> _owners;

  internal FragmentOwnershipIndex(IEnumerable<CpgFragmentOwnership> owners)
  {
    ArgumentNullException.ThrowIfNull(owners);
    _owners = owners
      .OrderBy(owner => owner.SpanLength)
      .ThenBy(owner => owner.SpanStart)
      .ThenBy(owner => owner.SourceOrder)
      .ToArray();
  }

  internal CpgFragmentOwnership? FindOwner(CpgNodeDescriptor descriptor)
  {
    ArgumentNullException.ThrowIfNull(descriptor);
    return FindOwner(descriptor.SpanStart, descriptor.SpanEnd);
  }

  internal CpgFragmentOwnership? FindOwner(int? spanStart, int? spanEnd)
  {
    if (!spanStart.HasValue || !spanEnd.HasValue)
    {
      return null;
    }

    return _owners.FirstOrDefault(owner =>
      spanStart.Value >= owner.SpanStart && spanEnd.Value <= owner.SpanEnd);
  }
}

internal sealed record CpgFragmentOwnership(
  string Kind,
  int SpanStart,
  int SpanEnd,
  int SourceOrder)
{
  internal int SpanLength => SpanEnd - SpanStart;
}

/// <summary>
/// Materializes each frozen graph node's fragment ownership once for a build.
/// </summary>
internal sealed class FragmentNodeOwnershipIndex
{
  private readonly IReadOnlyDictionary<NodeId, CpgFragmentOwnership?> _ownersByNodeId;
  private readonly IReadOnlyDictionary<CpgFragmentOwnership, IReadOnlySet<NodeId>> _nodeIdsByOwner;

  private FragmentNodeOwnershipIndex(
    IReadOnlyDictionary<NodeId, CpgFragmentOwnership?> ownersByNodeId,
    IReadOnlyDictionary<CpgFragmentOwnership, IReadOnlySet<NodeId>> nodeIdsByOwner,
    IReadOnlySet<NodeId> skeletonNodeIds)
  {
    _ownersByNodeId = ownersByNodeId;
    _nodeIdsByOwner = nodeIdsByOwner;
    SkeletonNodeIds = skeletonNodeIds;
  }

  internal IReadOnlySet<NodeId> SkeletonNodeIds { get; }

  internal static FragmentNodeOwnershipIndex Create(
    IEnumerable<RoslynCpgNode> nodes,
    FragmentOwnershipIndex ownership)
  {
    ArgumentNullException.ThrowIfNull(nodes);
    ArgumentNullException.ThrowIfNull(ownership);
    var ownersByNodeId = new Dictionary<NodeId, CpgFragmentOwnership?>();
    var nodeIdsByOwner = new Dictionary<CpgFragmentOwnership, HashSet<NodeId>>();
    var skeletonNodeIds = new HashSet<NodeId>();
    foreach (var node in nodes)
    {
      if (!node.NodeId.HasValue)
      {
        throw new InvalidOperationException("Frozen CPG nodes must have stable NodeIds.");
      }

      var nodeId = node.NodeId.Value;
      var owner = ownership.FindOwner(node.SpanStart, node.SpanEnd);
      ownersByNodeId.Add(nodeId, owner);
      if (owner is null)
      {
        skeletonNodeIds.Add(nodeId);
        continue;
      }

      if (!nodeIdsByOwner.TryGetValue(owner, out var nodeIds))
      {
        nodeIds = new HashSet<NodeId>();
        nodeIdsByOwner.Add(owner, nodeIds);
      }

      nodeIds.Add(nodeId);
    }

    return new FragmentNodeOwnershipIndex(
      ownersByNodeId,
      nodeIdsByOwner.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<NodeId>)entry.Value),
      skeletonNodeIds);
  }

  internal IReadOnlySet<NodeId> GetNodeIds(CpgFragmentOwnership owner)
  {
    ArgumentNullException.ThrowIfNull(owner);
    return _nodeIdsByOwner.TryGetValue(owner, out var nodeIds)
      ? nodeIds
      : new HashSet<NodeId>();
  }

  internal CpgFragmentOwnership? GetOwner(NodeId nodeId)
  {
    return _ownersByNodeId.GetValueOrDefault(nodeId);
  }
}
