namespace Domain.Rules;

/// <summary>
/// 表示规则传播许可。
/// </summary>
public enum RulePropagationAllowance
{
    None = 0,
    SameTypeOnly = 1,
    CallPropagation = 2,
    DependencyPropagation = 3,
    ClosureExpansion = 4,
}
