using RoslynPrototype.Marking;

namespace RoslynPrototype.Lifting;

/// <summary>
/// 表示 Mark Lifting 阶段产生的结构候选，以及它来自哪个原始种子标记。
/// </summary>
public sealed record LiftedMarkRecord(
  string RuleId,
  MarkRecord Mark,
  MarkRecord SourceMark,
  int Depth,
  string? GroupKey = null);
