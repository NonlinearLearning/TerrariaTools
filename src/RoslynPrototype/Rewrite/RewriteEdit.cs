using Microsoft.CodeAnalysis.Text;

namespace RoslynPrototype.Rewrite;

/// <summary>
/// 表示一次改写在某个文件上的最小文本编辑。
/// </summary>
public sealed record RewriteEdit(
  /// <summary>
  /// 被改写文件的路径。
  /// </summary>
  string FilePath,
  /// <summary>
  /// 原始源码中被替换或删除的文本跨度。
  /// </summary>
  TextSpan Span,
  /// <summary>
  /// 改写前的原始文本。
  /// </summary>
  string OriginalText,
  /// <summary>
  /// 改写后的替换文本；删除时为空字符串。
  /// </summary>
  string ReplacementText);
