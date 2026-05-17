namespace RoslynPrototype.Rewrite;

public sealed record PrototypeRewriteResult(
  string RewrittenSource,
  IReadOnlyList<RewriteEdit> Edits,
  string DiffText);
