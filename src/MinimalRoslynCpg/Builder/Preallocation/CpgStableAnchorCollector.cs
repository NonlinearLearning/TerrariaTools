using MinimalRoslynCpg.Builder.Streaming;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Preallocation;

/// <summary>
/// Collects immutable descriptors before global NodeId allocation.
/// </summary>
internal sealed class CpgStableAnchorCollector
{
  private readonly HashSet<StableNodeAnchor> _anchors = new();

  internal void Add(CpgNodeDescriptor descriptor)
  {
    ArgumentNullException.ThrowIfNull(descriptor);
    Add(descriptor.Anchor);
  }

  internal void Add(StableNodeAnchor anchor)
  {
    _anchors.Add(anchor);
  }

  internal DeterministicNodeIdTable CreateAllocation()
  {
    return DeterministicNodeIdTable.Create(_anchors);
  }
}
