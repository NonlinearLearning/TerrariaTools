using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Streaming;

/// 单个 CPG 节点的不可变且独立于图的描述。
public readonly record struct CpgNodeDescriptor(
  StableNodeAnchor Anchor,
  NLCPGNodeKind Kind,
  uint NameId,
  uint FullNameId,
  uint SignatureId,
  NLCPGDispatchKind? DispatchKind,
  uint TypeFullNameId,
  uint FilePathId,
  int? SpanStart,
  int? SpanEnd,
  bool IsImplicit)
{
    internal static CpgNodeDescriptor FromNode(NLCPGNode node)
    {
        return new CpgNodeDescriptor(
          node.StableAnchor ?? throw new InvalidOperationException("Streaming node descriptors require stable anchors."),
          node.Kind,
          node.NameId,
          node.FullNameId,
          node.SignatureId,
          node.DispatchKind,
          node.TypeFullNameId,
          node.FilePathId,
          node.SpanStart,
          node.SpanEnd,
          node.IsImplicit);
    }

    internal NLCPGNode Materialize(DeterministicNodeIdTable allocation)
    {
        return new NLCPGNode(
          Kind,
          NameId,
          FullNameId,
          SignatureId,
          DispatchKind,
          TypeFullNameId,
          FilePathId,
          SpanStart,
          SpanEnd,
          IsImplicit: IsImplicit,
          NodeId: allocation.GetRequiredId(Anchor),
          StableAnchor: Anchor);
    }
}
