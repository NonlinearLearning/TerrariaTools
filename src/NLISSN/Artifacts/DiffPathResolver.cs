namespace NLISSN.Artifacts;

/// <summary>
/// Maps one source file to its deterministic diff artifact path.
/// </summary>
internal static class DiffPathResolver
{
  internal static string ResolveFileDiffPath(string inputRootPath, string filePath, string diffRootPath)
  {
    string relativePath = Path.GetRelativePath(inputRootPath, filePath);
    string? relativeDirectory = Path.GetDirectoryName(relativePath);
    string fileName = Path.GetFileNameWithoutExtension(relativePath);
    string targetDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
      ? diffRootPath
      : Path.Combine(diffRootPath, relativeDirectory);
    return Path.Combine(targetDirectory, $"{fileName}.rewrite.diff");
  }

  internal static string ResolveFileDiffPath(
    string inputRootPath,
    string filePath,
    string diffRootPath,
    RuleDiffCategory category)
  {
    string relativePath = Path.GetRelativePath(inputRootPath, filePath);
    string? relativeDirectory = Path.GetDirectoryName(relativePath);
    string fileName = Path.GetFileNameWithoutExtension(relativePath);
    string targetDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
      ? Path.Combine(diffRootPath, category.ToString())
      : Path.Combine(diffRootPath, category.ToString(), relativeDirectory);
    return Path.Combine(targetDirectory, $"{fileName}.rewrite.diff");
  }
}
