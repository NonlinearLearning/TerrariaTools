namespace RoslynPrototype.Application;

internal static class DeletionDiffPathResolver
{
    internal static string ResolveDiffPath(
      string inputPath,
      IReadOnlyDictionary<string, string> options)
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

    internal static string ResolveDirectoryDiffRoot(
      string inputPath,
      IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("diff-out", out var explicitPath) &&
            !string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        return inputPath;
    }

    internal static string ResolveFileDiffPath(
      string inputRootPath,
      string filePath,
      string diffRootPath)
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
