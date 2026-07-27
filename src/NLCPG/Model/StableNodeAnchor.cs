using NLCPG.Contracts;

namespace NLCPG.Model;

//使其Node实例拥有唯一身份
public readonly record struct StableNodeAnchor(NLCPGNodeKind Kind, uint FilePathId, int SpanStart, int SpanEnd, StableNodeRole Role, int Ordinal, uint ExtraKeyId)
{
    // 在节点未携带稳定锚点时，根据位置与文本信息生成后备锚点。
    public static StableNodeAnchor CreateFallback(NLCPGNode node, StringInterner interner, StableNodeRole role)
    {
        ArgumentNullException.ThrowIfNull(interner);

        return new StableNodeAnchor(
          node.Kind,
          interner.Intern(node.FilePath ?? string.Empty),
          node.SpanStart ?? -1,
          node.SpanEnd ?? -1,
          role,
          0,
          interner.Intern(node.FullName ?? node.Signature ?? node.Name ?? node.DisplayKind));
    }
}
