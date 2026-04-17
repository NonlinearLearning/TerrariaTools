using System.Text.RegularExpressions;

namespace Analysis.X2Cpg;

/// <summary>
/// 查找和过滤源文件。
///
/// 对应 Joern `SourceFiles.scala` 的核心行为。
/// </summary>
public static class SourceFiles
{
    public const long DefaultMaxFileSizeBytes = int.MaxValue - 2L;

    public static bool FilterFile(
        string file,
        string inputPath,
        IEnumerable<Regex>? ignoredDefaultRegex = null,
        Regex? ignoredFilesRegex = null,
        IEnumerable<string>? ignoredFilesPath = null)
    {
        if (IsTooLarge(file))
        {
            return false;
        }

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

    public static IReadOnlyList<string> FilterFiles(
        IEnumerable<string> files,
        string inputPath,
        IEnumerable<Regex>? ignoredDefaultRegex = null,
        Regex? ignoredFilesRegex = null,
        IEnumerable<string>? ignoredFilesPath = null)
    {
        return files.Where(file => FilterFile(file, inputPath, ignoredDefaultRegex, ignoredFilesRegex, ignoredFilesPath)).ToArray();
    }

    public static IReadOnlyList<string> Determine(
        string inputPath,
        ISet<string> sourceFileExtensions,
        IEnumerable<Regex>? ignoredDefaultRegex = null,
        Regex? ignoredFilesRegex = null,
        IEnumerable<string>? ignoredFilesPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(sourceFileExtensions);

        return Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories)
            .Where(file => sourceFileExtensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Where(file => FilterFile(file, inputPath, ignoredDefaultRegex, ignoredFilesRegex, ignoredFilesPath))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTooLarge(string file)
    {
        FileInfo fileInfo = new(file);
        return fileInfo.Exists && fileInfo.Length > DefaultMaxFileSizeBytes;
    }

    private static bool IsSameOrUnder(string file, string ignorePath)
    {
        string fullFile = Path.GetFullPath(file);
        string fullIgnore = Path.GetFullPath(ignorePath);
        return string.Equals(fullFile, fullIgnore, StringComparison.OrdinalIgnoreCase) ||
               fullFile.StartsWith(fullIgnore + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
