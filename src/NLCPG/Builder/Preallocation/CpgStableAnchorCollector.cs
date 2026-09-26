using NLCPG.Builder.Streaming;
using NLCPG.Model;

namespace NLCPG.Builder.Preallocation;

/// 在全局 NodeId 分配前收集不可变描述符。
/// 为什么会放在这里
internal sealed class CpgStableAnchorCollector
{
    private readonly HashSet<StableNodeAnchor> _anchors = new();

    internal void Add(CpgNodeDescriptor descriptor)
    {
        Add(descriptor.Anchor);
    }

    internal void Add(StableNodeAnchor anchor)
    {
        _anchors.Add(anchor);
    }

    internal int Count => _anchors.Count;

    internal IReadOnlyCollection<StableNodeAnchor> Anchors => _anchors;

    internal DeterministicNodeIdTable CreateAllocation()
    {
        return DeterministicNodeIdTable.Create(_anchors);
    }
}
