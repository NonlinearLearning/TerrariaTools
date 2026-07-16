using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

public readonly record struct StableNodeAnchor(
  RoslynCpgNodeKind Kind,
  uint FilePathId,
  int SpanStart,
  int SpanEnd,
  StableNodeRole Role,
  int Ordinal,
  uint ExtraKeyId)
{
  public static StableNodeAnchor CreateFallback(
    RoslynCpgNode node,
    StringInterner interner,
    StableNodeRole role)
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
