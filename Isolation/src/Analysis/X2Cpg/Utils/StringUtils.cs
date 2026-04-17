namespace Analysis.X2Cpg.Utils;

/// <summary>
/// 字符串辅助扩展。
///
/// 对应 Joern `StringUtils.scala`。
/// </summary>
public static class StringUtils
{
    public static bool IsAllUpperCase(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.All(character => char.IsUpper(character) || !char.IsLetter(character));
    }
}
