using System.Text.RegularExpressions;
using Logic.Analysis.Engine.X2Cpg;

namespace Infrastructure.Analysis.Engine.X2Cpg;

/// <summary>
/// 查找和过滤源文件。
///
/// 对应 Joern `SourceFiles.scala` 的核心行为。
/// </summary>
public static class SourceFiles
{
    public const long DefaultMaxFileSizeBytes = SourceFileDiscoveryRules.DefaultMaxFileSizeBytes;

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

        return SourceFileFilter.IsIncludedByRules(
            file,
            inputPath,
            ignoredDefaultRegex,
            ignoredFilesRegex,
            ignoredFilesPath);
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

        return SourceFileDiscoveryRules.OrderFiles(Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories)
            .Where(file => SourceFileDiscoveryRules.HasSupportedExtension(file, sourceFileExtensions))
            .Where(file => FilterFile(file, inputPath, ignoredDefaultRegex, ignoredFilesRegex, ignoredFilesPath)));
    }

    private static bool IsTooLarge(string file)
    {
        FileInfo fileInfo = new(file);
        return fileInfo.Exists && !SourceFileDiscoveryRules.IsFileSizeAllowed(fileInfo.Length);
    }
}
