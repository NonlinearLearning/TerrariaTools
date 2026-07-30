using System.Text;
using System.Text.Json;
using NLISSN.Core.Decision;

namespace NLISSN.Artifacts;

internal static class AnalysisEvidenceArtifactService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  public static void Write(string path, AnalysisEvidenceGraph graph)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(graph);
    if (File.Exists(path))
    {
      throw new InvalidOperationException($"Evidence output already exists: {path}");
    }

    var directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var temporaryPath = path + ".tmp";
    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(graph, JsonOptions), new UTF8Encoding(false));
    File.Move(temporaryPath, path);
  }
}
