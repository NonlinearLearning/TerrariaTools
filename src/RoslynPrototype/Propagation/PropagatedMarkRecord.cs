using RoslynPrototype.Marking;

namespace RoslynPrototype.Propagation;

/// <summary>
/// 表示一次传播产生的标记，以及它来自哪个种子标记。
/// </summary>
public sealed record PropagatedMarkRecord(
  /// <summary>
  /// 产生这条传播标记的规则标识。
  /// </summary>
    string RuleId,
  /// <summary>
  /// 传播后实际落到的新标记。
  /// </summary>
    MarkRecord Mark,
  /// <summary>
  /// 触发本次传播的源种子标记。
  /// </summary>
    MarkRecord SourceMark,
  /// <summary>
  /// 从源种子标记传播到当前标记的层级深度。
  /// </summary>
    int Depth,
  /// <summary>
  /// 阶段之间共享的规则分组键；为空时回退到 RuleId。
  /// </summary>
    string? GroupKey = null,
  /// <summary>
  /// 传播阶段额外收集的结构化中间事实；为空时表示只有简单传播标记。
  /// </summary>
    object? Payload = null);
