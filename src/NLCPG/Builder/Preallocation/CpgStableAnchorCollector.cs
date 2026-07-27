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
