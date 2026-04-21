namespace Logic.Analysis.Engine.X2Cpg.Utils;

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

    /// <summary>
    /// 移除字符串首尾的单引号或双引号。
    /// </summary>
    /// <param name="value">目标字符串。</param>
    /// <returns>处理后的字符串。</returns>
    public static string StripQuotes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Trim('"', '\'');
    }
}
