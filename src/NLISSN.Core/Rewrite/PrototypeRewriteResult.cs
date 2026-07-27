namespace NLISSN.Core.Rewrite;

/// 包含由规则决策生成的可移植文本操作和显示编辑。
public sealed record PrototypeRewritePlan(
  IReadOnlyList<RewritePlanEdit> Operations,
  IReadOnlyList<RewriteEdit> Edits);

/// 封装一次改写执行后的源码结果、编辑列表和结构化 diff。
public sealed record PrototypeRewriteResult(
  /// 应用所有改写后的完整源码文本。
  string? RewrittenSource,
  /// 本次改写生成的最小编辑集合。
  IReadOnlyList<RewriteEdit> Edits,
  /// 结构化 diff 文档，作为 rewrite 子系统的主 diff 结果。
  DiffDocument Diff,
  /// 可回放的精确文本操作，不等同于展示用 diff 编辑。
  IReadOnlyList<RewritePlanEdit>? Operations = null)
{
  public DiffSummary DiffSummary => Diff.Summary;
}
