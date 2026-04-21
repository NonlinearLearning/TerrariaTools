namespace Domain.Rules;

/// <summary>
/// 表示规则失败模式。
/// </summary>
public enum RuleFailureMode
{
    Unknown = 0,
    Skip = 1,
    Warn = 2,
    BlockWorkflow = 3,
}
