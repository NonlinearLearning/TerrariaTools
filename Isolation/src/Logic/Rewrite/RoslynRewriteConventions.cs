namespace Logic.Rewrite;

/// <summary>
/// 收敛 Roslyn 代码重写中的纯决策规则。
/// </summary>
public static class RoslynRewriteConventions
{
    /// <summary>
    /// 判断清空方法体时是否使用空语句。
    /// </summary>
    public static bool ShouldUseEmptyStatement(bool isVoidReturnType)
    {
        return isVoidReturnType;
    }

    /// <summary>
    /// 判断清空方法体时是否使用 null 字面量。
    /// </summary>
    public static bool ShouldUseNullLiteral(string? predefinedKeywordText)
    {
        return string.Equals(predefinedKeywordText?.Trim(), "string", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断依赖是否属于当前类型。
    /// </summary>
    public static bool IsClassLocalDependency(string? containingTypeName, string? className)
    {
        return !string.IsNullOrWhiteSpace(containingTypeName) &&
               !string.IsNullOrWhiteSpace(className) &&
               string.Equals(containingTypeName.Trim(), className.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断标识符是否需要在渲染影子类时替换。
    /// </summary>
    public static bool ShouldRewriteIdentifier(string? identifier, string? sourceClassName)
    {
        return !string.IsNullOrWhiteSpace(identifier) &&
               !string.IsNullOrWhiteSpace(sourceClassName) &&
               string.Equals(identifier.Trim(), sourceClassName.Trim(), StringComparison.Ordinal);
    }
}
