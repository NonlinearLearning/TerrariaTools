using RoslynPrototype.Testing.TestCodeSet;

namespace RoslynPrototype.Testing.TestInfrastructure;

public sealed class TestWorkspaceWriter
{
  public TestWorkspace Write(TestAsset asset)
  {
    ArgumentNullException.ThrowIfNull(asset);

    var rootPath = Path.Combine(
      Path.GetTempPath(),
      "RoslynDeletionPrototype.Tests",
      asset.Id,
      Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(rootPath);

    try
    {
      foreach (var (relativePath, contents) in asset.Files)
      {
        ValidateRelativePath(relativePath);

        var outputPath = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, contents);
      }

      return new TestWorkspace(rootPath);
    }
    catch
    {
      Directory.Delete(rootPath, recursive: true);
      throw;
    }
  }

  private static void ValidateRelativePath(string relativePath)
  {
    if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
    {
      throw new ArgumentException("Test asset file paths must be relative.", nameof(relativePath));
    }

    var segments = relativePath.Split(['/', '\\'], StringSplitOptions.None);
    if (segments.Any(segment => segment == ".."))
    {
      throw new ArgumentException("Test asset file paths cannot contain '..' segments.", nameof(relativePath));
    }
  }
}
