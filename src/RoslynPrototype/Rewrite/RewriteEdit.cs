using Microsoft.CodeAnalysis.Text;

namespace RoslynPrototype.Rewrite;

public sealed record RewriteEdit(
  string FilePath,
  TextSpan Span,
  string OriginalText,
  string ReplacementText);
