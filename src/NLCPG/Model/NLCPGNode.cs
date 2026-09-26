using NLCPG.Contracts;

namespace NLCPG.Model;

/// 表示 NLCPG 中的一个紧凑节点值；文本字段由所属图的字符串表解析。
public readonly record struct NLCPGNode(
  NLCPGNodeKind Kind,
  uint NameId = 0,
  uint FullNameId = 0,
  uint SignatureId = 0,
  NLCPGDispatchKind? DispatchKind = null,
  uint TypeFullNameId = 0,
  uint FilePathId = 0,
  int? SpanStart = null,
  int? SpanEnd = null,
  bool IsImplicit = false,
  NodeId? NodeId = null,
  StableNodeAnchor? StableAnchor = null)
{
  public bool Equals(NLCPGNode other)
  {
    if (StableAnchor.HasValue || other.StableAnchor.HasValue)
    {
      return StableAnchor.HasValue && other.StableAnchor.HasValue &&
        StableAnchor.Value.Equals(other.StableAnchor.Value);
    }

    if (NodeId.HasValue || other.NodeId.HasValue)
    {
      return NodeId.HasValue && other.NodeId.HasValue && NodeId.Value.Equals(other.NodeId.Value);
    }

    return Kind == other.Kind &&
      NameId == other.NameId &&
      FullNameId == other.FullNameId &&
      SignatureId == other.SignatureId &&
      DispatchKind == other.DispatchKind &&
      TypeFullNameId == other.TypeFullNameId &&
      FilePathId == other.FilePathId &&
      SpanStart == other.SpanStart &&
      SpanEnd == other.SpanEnd &&
      IsImplicit == other.IsImplicit;
  }

  public override int GetHashCode()
  {
    if (StableAnchor.HasValue)
    {
      return StableAnchor.Value.GetHashCode();
    }

    if (NodeId.HasValue)
    {
      return NodeId.Value.GetHashCode();
    }

    var hash = new HashCode();
    hash.Add(Kind);
    hash.Add(NameId);
    hash.Add(FullNameId);
    hash.Add(SignatureId);
    hash.Add(DispatchKind);
    hash.Add(TypeFullNameId);
    hash.Add(FilePathId);
    hash.Add(SpanStart);
    hash.Add(SpanEnd);
    hash.Add(IsImplicit);
    return hash.ToHashCode();
  }
}
