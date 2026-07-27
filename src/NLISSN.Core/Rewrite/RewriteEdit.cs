using Microsoft.CodeAnalysis.Text;

namespace NLISSN.Core.Rewrite;

/// 表示一次改写在某个文件上的最小文本编辑。
public sealed record RewriteEdit(
  /// 被改写文件的路径。
  string FilePath,
  /// 原始源码中被替换或删除的文本跨度。
  TextSpan Span,
  /// 改写前的原始文本。
  string OriginalText,
  /// 改写后的替换文本；删除时为空字符串。
  string ReplacementText);
