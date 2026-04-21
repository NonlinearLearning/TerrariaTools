using Logic.Analysis.Engine.Frontend;

namespace Logic.Rewrite;

/// <summary>
/// 收敛 Roslyn 成员切片与成员命名的纯规则。
/// </summary>
public static class RoslynSliceConventions
{
    /// <summary>
    /// 构造字段成员名列表。
    /// </summary>
    public static string BuildFieldMemberName(IEnumerable<string> variableNames)
    {
        ArgumentNullException.ThrowIfNull(variableNames);
        return string.Join(",", variableNames);
    }

    /// <summary>
    /// 构造成员名兜底值。
    /// </summary>
    public static string BuildFallbackMemberName(string? memberKindName)
    {
        return string.IsNullOrWhiteSpace(memberKindName)
            ? FrontendGraphConventions.Unknown
            : memberKindName.Trim();
    }
}
