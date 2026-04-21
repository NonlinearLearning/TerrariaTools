namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛匿名对象成员命名规则。
/// </summary>
public static class AnonymousObjectConventions
{
    /// <summary>
    /// 匿名对象成员来源标记。
    /// </summary>
    public const string AnonymousObjectMemberSource = "AnonymousObjectMember";

    /// <summary>
    /// 构造匿名对象成员名称。
    /// </summary>
    public static string BuildMemberName(
        string? explicitName,
        string? identifierName,
        string? memberAccessName,
        string? fallbackExpressionText)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(identifierName))
        {
            return identifierName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(memberAccessName))
        {
            return memberAccessName.Trim();
        }

        return string.IsNullOrWhiteSpace(fallbackExpressionText)
            ? FrontendGraphConventions.Unknown
            : fallbackExpressionText.Trim();
    }
}
