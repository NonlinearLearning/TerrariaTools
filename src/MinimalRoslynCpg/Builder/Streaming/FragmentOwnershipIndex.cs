using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Selects one fragment owner for each source span without consulting graph state.
/// </summary>
internal sealed class FragmentOwnershipIndex
{
  private readonly IReadOnlyList<OwnershipStartGroup> _groups;

  internal FragmentOwnershipIndex(IEnumerable<CpgFragmentOwnership> owners)
  {
    ArgumentNullException.ThrowIfNull(owners);
    var groups = owners
      .GroupBy(owner => owner.SpanStart)
      .OrderBy(group => group.Key)
      .Select(group => new OwnershipStartGroup(
        group.Key,
        group
          .OrderBy(owner => owner.SpanLength)
          .ThenBy(owner => owner.SourceOrder)
          .ToArray()))
      .ToArray();
    var maximumSpanEnd = int.MinValue;
    foreach (var group in groups)
    {
      maximumSpanEnd = Math.Max(maximumSpanEnd, group.MaximumSpanEnd);
      group.PrefixMaximumSpanEnd = maximumSpanEnd;
    }

    _groups = groups;
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

    var groupIndex = FindLastStartGroup(spanStart.Value);
    CpgFragmentOwnership? bestOwner = null;
    while (groupIndex >= 0)
    {
      var group = _groups[groupIndex];
      if (group.MaximumSpanEnd >= spanEnd.Value)
      {
        foreach (var owner in group.Owners)
        {
          if (owner.SpanEnd < spanEnd.Value)
          {
            continue;
          }

          if (bestOwner is null || IsPreferred(owner, bestOwner))
          {
            bestOwner = owner;
          }
        }
      }

      if (groupIndex == 0 || _groups[groupIndex - 1].PrefixMaximumSpanEnd < spanEnd.Value)
      {
        break;
      }

      groupIndex -= 1;
    }

    return bestOwner;
  }

  private int FindLastStartGroup(int spanStart)
  {
    var low = 0;
    var high = _groups.Count - 1;
    var result = -1;
    while (low <= high)
    {
      var middle = low + ((high - low) / 2);
      if (_groups[middle].SpanStart <= spanStart)
      {
        result = middle;
        low = middle + 1;
      }
      else
      {
        high = middle - 1;
      }
    }

    return result;
  }

  private static bool IsPreferred(CpgFragmentOwnership candidate, CpgFragmentOwnership current)
  {
    return candidate.SpanLength < current.SpanLength ||
      (candidate.SpanLength == current.SpanLength && candidate.SpanStart < current.SpanStart) ||
      (candidate.SpanLength == current.SpanLength && candidate.SpanStart == current.SpanStart &&
       candidate.SourceOrder < current.SourceOrder);
  }

  private sealed class OwnershipStartGroup
  {
    internal OwnershipStartGroup(int spanStart, IReadOnlyList<CpgFragmentOwnership> owners)
    {
      SpanStart = spanStart;
      Owners = owners;
      MaximumSpanEnd = owners.Max(owner => owner.SpanEnd);
    }

    internal int SpanStart { get; }

    internal IReadOnlyList<CpgFragmentOwnership> Owners { get; }

    internal int MaximumSpanEnd { get; }

    internal int PrefixMaximumSpanEnd { get; set; }
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
