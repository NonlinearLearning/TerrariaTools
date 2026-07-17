namespace RoslynPrototype.Rewrite;

/// <summary>
/// 封装一次改写执行后的源码结果、编辑列表和结构化 diff。
/// </summary>
public sealed record PrototypeRewriteResult(
  /// <summary>
  /// 应用所有改写后的完整源码文本。
  /// </summary>
  string? RewrittenSource,
  /// <summary>
  /// 本次改写生成的最小编辑集合。
  /// </summary>
  IReadOnlyList<RewriteEdit> Edits,
  /// <summary>
  /// 结构化 diff 文档，作为 rewrite 子系统的主 diff 结果。
  /// </summary>
  DiffDocument Diff)
{
  public DiffSummary DiffSummary => Diff.Summary;
}
