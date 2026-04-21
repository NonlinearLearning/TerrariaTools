namespace Domain.Rules;

/// <summary>
/// 表示规则冲突模式。
/// </summary>
public enum RuleConflictMode
{
    Unknown = 0,
    PreferHigherPriority = 1,
    BlockOnConflict = 2,
    MergeReasons = 3,
    KeepAllForReview = 4,
}
