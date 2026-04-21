namespace Domain.Rules;

/// <summary>
/// 表示规则目标种类。
/// </summary>
public enum RuleTargetKind
{
    Unknown = 0,
    Class = 1,
    Method = 2,
    Member = 3,
    Statement = 4,
    Closure = 5,
    ShadowType = 6,
}
