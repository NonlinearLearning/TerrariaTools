using System.Text.RegularExpressions;

namespace Logic.Analysis.Engine.X2Cpg;

/// <summary>
/// 提供源文件忽略规则的纯逻辑判断。
/// </summary>
public static class SourceFileFilter
{
    /// <summary>
    /// 判断文件是否通过忽略规则。
    /// </summary>
    /// <param name="file">待检查文件路径。</param>
    /// <param name="inputPath">输入根路径。</param>
    /// <param name="ignoredDefaultRegex">默认忽略正则。</param>
    /// <param name="ignoredFilesRegex">用户忽略正则。</param>
    /// <param name="ignoredFilesPath">用户忽略路径集合。</param>
    /// <returns>通过规则时返回 true。</returns>
    public static bool IsIncludedByRules(
        string file,
        string inputPath,
        IEnumerable<Regex>? ignoredDefaultRegex = null,
        Regex? ignoredFilesRegex = null,
        IEnumerable<string>? ignoredFilesPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        string relativePath = Path.GetRelativePath(inputPath, file);
        if (ignoredDefaultRegex?.Any(regex => regex.IsMatch(relativePath)) == true)
        {
            return false;
        }

        if (ignoredFilesRegex?.IsMatch(relativePath) == true)
        {
            return false;
        }

        if (ignoredFilesPath?.Any(ignorePath => IsSameOrUnder(file, ignorePath)) == true)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 按忽略规则过滤文件集合。
    /// </summary>
    /// <param name="files">待过滤文件集合。</param>
    /// <param name="inputPath">输入根路径。</param>
    /// <param name="ignoredDefaultRegex">默认忽略正则。</param>
    /// <param name="ignoredFilesRegex">用户忽略正则。</param>
    /// <param name="ignoredFilesPath">用户忽略路径集合。</param>
    /// <returns>通过规则的文件列表。</returns>
    public static IReadOnlyList<string> FilterByRules(
        IEnumerable<string> files,
        string inputPath,
        IEnumerable<Regex>? ignoredDefaultRegex = null,
        Regex? ignoredFilesRegex = null,
        IEnumerable<string>? ignoredFilesPath = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        return files
            .Where(file => IsIncludedByRules(file, inputPath, ignoredDefaultRegex, ignoredFilesRegex, ignoredFilesPath))
            .ToArray();
    }

    private static bool IsSameOrUnder(string file, string ignorePath)
    {
        string fullFile = Path.GetFullPath(file);
        string fullIgnore = Path.GetFullPath(ignorePath);
        return string.Equals(fullFile, fullIgnore, StringComparison.OrdinalIgnoreCase) ||
               fullFile.StartsWith(fullIgnore + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
