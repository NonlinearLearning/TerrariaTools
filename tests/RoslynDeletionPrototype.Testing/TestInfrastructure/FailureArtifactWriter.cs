using System.Text.Json;
using RoslynPrototype.Testing.TestCodeSet.Cpg;

namespace RoslynPrototype.Testing.TestInfrastructure;

public static class FailureArtifactWriter
{
  public static string Write(
    string root,
    GeneratedCSharpFixture fixture,
    IReadOnlyDictionary<string, string> options,
    CpgExecutionSnapshot snapshot)
  {
    return Write(
      root,
      fixture,
      options,
      new Dictionary<string, CpgExecutionSnapshot>(StringComparer.Ordinal)
      {
        ["serial"] = snapshot,
      });
  }

  public static string Write(
    string root,
    GeneratedCSharpFixture fixture,
    IReadOnlyDictionary<string, string> options,
    IReadOnlyDictionary<string, CpgExecutionSnapshot> snapshots)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(root);
    ArgumentNullException.ThrowIfNull(fixture);
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(snapshots);

    var artifactRoot = Path.Combine(root, fixture.Id, Guid.NewGuid().ToString("N"));
    var inputsRoot = Path.Combine(artifactRoot, "inputs");
    foreach (var file in fixture.Files.OrderBy(item => item.Key, StringComparer.Ordinal))
    {
      var relativePath = file.Key.Replace('/', Path.DirectorySeparatorChar);
      if (Path.IsPathRooted(relativePath) ||
          relativePath.Split(Path.DirectorySeparatorChar).Any(segment => segment is "" or "." or ".."))
      {
        throw new ArgumentException($"Fixture path must be a relative child path: {file.Key}", nameof(fixture));
      }

      var outputPath = Path.Combine(inputsRoot, relativePath);
      Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
      File.WriteAllText(outputPath, file.Value);
    }

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(
      Path.Combine(artifactRoot, "options.json"),
      JsonSerializer.Serialize(options.OrderBy(item => item.Key, StringComparer.Ordinal), jsonOptions));
    File.WriteAllText(
      Path.Combine(artifactRoot, "replay.json"),
      JsonSerializer.Serialize(
        new
        {
          fixture.Id,
          Options = options.OrderBy(item => item.Key, StringComparer.Ordinal),
        },
        jsonOptions));

    var snapshotsRoot = Path.Combine(artifactRoot, "snapshots");
    foreach (var snapshot in snapshots.OrderBy(item => item.Key, StringComparer.Ordinal))
    {
      if (string.IsNullOrWhiteSpace(snapshot.Key) ||
          snapshot.Key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
          snapshot.Key is "." or "..")
      {
        throw new ArgumentException($"Snapshot name must be a valid file name: {snapshot.Key}", nameof(snapshots));
      }

      Directory.CreateDirectory(snapshotsRoot);
      File.WriteAllText(
        Path.Combine(snapshotsRoot, $"{snapshot.Key}.json"),
        JsonSerializer.Serialize(snapshot.Value, jsonOptions));
    }

    File.WriteAllText(
      Path.Combine(artifactRoot, "snapshot.json"),
      JsonSerializer.Serialize(snapshots.OrderBy(item => item.Key, StringComparer.Ordinal), jsonOptions));
    return artifactRoot;
  }
}
