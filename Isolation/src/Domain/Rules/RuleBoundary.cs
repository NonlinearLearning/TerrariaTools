namespace Domain.Rules;

/// <summary>
/// 表示规则边界。
/// </summary>
public enum RuleBoundary
{
    Unknown = 0,
    CurrentMember = 1,
    CurrentType = 2,
    CurrentDocument = 3,
    CurrentProject = 4,
    CurrentWorkspace = 5,
}
