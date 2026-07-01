namespace RoslynPrototype.Rewrite;

/// <summary>
/// 封装一次改写执行后的源码结果、编辑列表和差异文本。
/// </summary>
public sealed record PrototypeRewriteResult(
  /// <summary>
  /// 应用所有改写后的完整源码文本。
  /// </summary>
  string RewrittenSource,
  /// <summary>
  /// 本次改写生成的最小编辑集合。
  /// </summary>
  IReadOnlyList<RewriteEdit> Edits,
  /// <summary>
  /// 面向调试和落盘的文本差异摘要。
  /// </summary>
  string DiffText);
