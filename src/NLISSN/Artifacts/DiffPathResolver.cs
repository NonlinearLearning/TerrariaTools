using NLISSN.Application;
namespace NLISSN.Artifacts;

/// 在遵循 <c>--diff-out</c> 的前提下，将分析输入映射为确定性的差异文件路径。
internal static class  DiffPathResolver
{
    internal static string ResolveDiffPath(string inputPath, IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("diff-out", out var explicitPath) &&
            !string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var directory = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(directory, $"{fileName}.rewrite.diff");
    }

    internal static string ResolveDirectoryDiffRoot(string inputPath, IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("diff-out", out var explicitPath) &&
            !string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        return inputPath;
    }

    internal static string ResolveFileDiffPath(string inputRootPath, string filePath, string diffRootPath)
    {
        var relativePath = Path.GetRelativePath(inputRootPath, filePath);
        var relativeDirectory = Path.GetDirectoryName(relativePath);
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var targetDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
          ? diffRootPath
          : Path.Combine(diffRootPath, relativeDirectory);
        return Path.Combine(targetDirectory, $"{fileName}.rewrite.diff");
    }
}
