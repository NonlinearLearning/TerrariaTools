using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NLISSN.Core.Decision;
using NLISSN.Infrastructure.Configuration;

namespace NLISSN.Artifacts;

internal static class AnalysisEvidenceArtifactService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  public static void Write(
    string path,
    AnalysisEvidenceGraph graph,
    AnalysisConfiguration configuration)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(configuration);
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
    var document = JsonSerializer.SerializeToNode(graph, JsonOptions)!.AsObject();
    document["configuration"] = JsonSerializer.SerializeToNode(
      ResolvedConfigurationArtifact.Create(configuration),
      JsonOptions);
    File.WriteAllText(temporaryPath, document.ToJsonString(JsonOptions), new UTF8Encoding(false));
    File.Move(temporaryPath, path);
  }

  public static void WriteReplayNotice(string path, AnalysisConfiguration configuration)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(configuration);
    var directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    File.WriteAllText(
      path,
      JsonSerializer.Serialize(
        new
        {
          kind = "rewrite-plan-replay",
          analysisEvidenceAvailable = false,
          reason = "Replay validates and applies an existing rewrite plan without rerunning analysis.",
          configuration = ResolvedConfigurationArtifact.Create(configuration)
        },
        JsonOptions),
      new UTF8Encoding(false));
  }
}
