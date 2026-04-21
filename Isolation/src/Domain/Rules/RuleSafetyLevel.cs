namespace Domain.Rules;

/// <summary>
/// 表示规则安全等级。
/// </summary>
public enum RuleSafetyLevel
{
    Unknown = 0,
    Conservative = 1,
    Balanced = 2,
    Aggressive = 3,
}
