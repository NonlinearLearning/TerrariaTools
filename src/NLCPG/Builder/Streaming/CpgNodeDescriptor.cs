using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Streaming;

/// 单个 CPG 节点的不可变且独立于图的描述。
internal sealed record CpgNodeDescriptor(
  StableNodeAnchor Anchor,
  NLCPGNodeKind Kind,
  string DisplayKind,
  string? Name,
  string? FullName,
  string? Signature,
  NLCPGDispatchKind? DispatchKind,
  string? TypeFullName,
  string? FilePath,
  int? SpanStart,
  int? SpanEnd,
  bool IsImplicit)
{
    internal static CpgNodeDescriptor FromNode(NLCPGNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new CpgNodeDescriptor(
          node.StableAnchor ?? throw new InvalidOperationException("Streaming node descriptors require stable anchors."),
          node.Kind,
          node.DisplayKind,
          node.Name,
          node.FullName,
          node.Signature,
          node.DispatchKind,
          node.TypeFullName,
          node.FilePath,
          node.SpanStart,
          node.SpanEnd,
          node.IsImplicit);
    }

    internal NLCPGNode Materialize(DeterministicNodeIdTable allocation)
    {
        return new NLCPGNode(
          Kind,
          DisplayKind,
          Name,
          FullName,
          Signature,
          DispatchKind,
          TypeFullName,
          FilePath,
          SpanStart,
          SpanEnd,
          Text: null,
          IsImplicit: IsImplicit,
          NodeId: allocation.GetRequiredId(Anchor),
          StableAnchor: Anchor);
    }
}
