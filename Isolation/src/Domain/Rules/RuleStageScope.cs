namespace Domain.Rules;

/// <summary>
/// 表示规则阶段范围。
/// </summary>
public enum RuleStageScope
{
    Unknown = 0,
    Marking = 1,
    Propagation = 2,
    Decision = 3,
    Planning = 4,
    Evidence = 5,
}
