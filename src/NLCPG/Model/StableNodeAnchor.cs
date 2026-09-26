using NLCPG.Contracts;

namespace NLCPG.Model;

//使其Node实例拥有唯一身份
public readonly record struct StableNodeAnchor(NLCPGNodeKind Kind, uint FilePathId, int SpanStart, int SpanEnd, StableNodeRole Role, int Ordinal, uint ExtraKeyId)
{
    // 在节点未携带稳定锚点时，根据位置与字符串表标识生成后备锚点。
    public static StableNodeAnchor CreateFallback(NLCPGNode node, StableNodeRole role)
    {
        return new StableNodeAnchor(
          node.Kind,
          node.FilePathId,
          node.SpanStart ?? -1,
          node.SpanEnd ?? -1,
          role,
          0,
          node.FullNameId != 0 ? node.FullNameId :
            node.SignatureId != 0 ? node.SignatureId : node.NameId);
    }
}
