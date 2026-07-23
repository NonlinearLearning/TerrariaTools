using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Streaming;

/// <summary>
/// Immutable, graph-independent description of one CPG node.
/// </summary>
internal sealed record CpgNodeDescriptor(
  StableNodeAnchor Anchor,
  RoslynCpgNodeKind Kind,
  string DisplayKind,
  string? Name,
  string? FullName,
  string? Signature,
  RoslynCpgDispatchKind? DispatchKind,
  string? TypeFullName,
  string? FilePath,
  int? SpanStart,
  int? SpanEnd,
  bool IsImplicit)
{
  internal static CpgNodeDescriptor FromNode(RoslynCpgNode node)
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

  internal RoslynCpgNode Materialize(DeterministicNodeIdTable allocation)
  {
    return new RoslynCpgNode(
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
