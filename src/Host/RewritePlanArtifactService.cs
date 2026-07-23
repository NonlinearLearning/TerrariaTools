using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application;

/// <summary>
/// Writes and validates portable rewrite-plan artifacts.
/// </summary>
public sealed class RewritePlanArtifactService
{
  internal const int SchemaVersion = 1;
  internal const string PlanFileName = "rewrite-plans.jsonl";
  internal const string ManifestFileName = "manifest.json";

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  public void Write(
    string artifactRoot,
    string inputRoot,
    int sourceFileCount,
    IReadOnlyList<RewritePlanFile> plans)
  {
    if (Directory.Exists(artifactRoot) || File.Exists(artifactRoot))
    {
      throw new InvalidOperationException($"Rewrite-plan output already exists: {artifactRoot}");
    }

    Directory.CreateDirectory(artifactRoot);
    var orderedPlans = plans.OrderBy(plan => plan.RelativePath, StringComparer.Ordinal).ToArray();
    var planPath = Path.Combine(artifactRoot, PlanFileName);
    var temporaryPlanPath = planPath + ".tmp";
    using (var stream = new FileStream(temporaryPlanPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
    {
      foreach (var plan in orderedPlans)
      {
        writer.WriteLine(JsonSerializer.Serialize(plan, JsonOptions));
      }
    }

    File.Move(temporaryPlanPath, planPath);
    var manifest = new RewritePlanManifest(
      SchemaVersion,
      "delete-class",
      Path.GetFullPath(inputRoot),
      sourceFileCount,
      orderedPlans.Length,
      PlanFileName,
      ComputeSha256(File.ReadAllBytes(planPath)));
    var manifestPath = Path.Combine(artifactRoot, ManifestFileName);
    var temporaryManifestPath = manifestPath + ".tmp";
    File.WriteAllText(temporaryManifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
    File.Move(temporaryManifestPath, manifestPath);
  }

  public (RewritePlanManifest Manifest, IReadOnlyList<RewritePlanFile> Plans) ReadAndValidate(
    string artifactRoot,
    string inputRoot)
  {
    var manifestPath = Path.Combine(artifactRoot, ManifestFileName);
    var manifest = JsonSerializer.Deserialize<RewritePlanManifest>(File.ReadAllText(manifestPath), JsonOptions)
      ?? throw new InvalidOperationException("Rewrite-plan manifest is empty.");
    if (manifest.SchemaVersion != SchemaVersion || manifest.PlanFile != PlanFileName)
    {
      throw new InvalidOperationException("Rewrite-plan manifest schema is not supported.");
    }

    var planPath = Path.Combine(artifactRoot, manifest.PlanFile);
    var bytes = File.ReadAllBytes(planPath);
    if (!string.Equals(manifest.PlanSha256, ComputeSha256(bytes), StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("Rewrite-plan file SHA-256 does not match the manifest.");
    }

    var plans = File.ReadLines(planPath)
      .Where(line => !string.IsNullOrWhiteSpace(line))
      .Select(line => JsonSerializer.Deserialize<RewritePlanFile>(line, JsonOptions)
        ?? throw new InvalidOperationException("Rewrite-plan record is empty."))
      .OrderBy(plan => plan.RelativePath, StringComparer.Ordinal)
      .ToArray();
    var seenPaths = new HashSet<string>(StringComparer.Ordinal);
    foreach (var plan in plans)
    {
      ValidatePlan(inputRoot, plan, seenPaths);
    }

    return (manifest, plans);
  }

  public static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

  private static void ValidatePlan(string inputRoot, RewritePlanFile plan, ISet<string> seenPaths)
  {
    if (Path.IsPathRooted(plan.RelativePath) || plan.RelativePath.Contains("..", StringComparison.Ordinal) ||
        !seenPaths.Add(plan.RelativePath))
    {
      throw new InvalidOperationException($"Rewrite-plan path is invalid: {plan.RelativePath}");
    }

    var sourcePath = Path.GetFullPath(Path.Combine(inputRoot, plan.RelativePath));
    var fullInputRoot = Path.GetFullPath(inputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!sourcePath.StartsWith(fullInputRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath))
    {
      throw new InvalidOperationException($"Rewrite-plan source does not exist: {plan.RelativePath}");
    }

    var source = File.ReadAllBytes(sourcePath);
    if (!string.Equals(plan.SourceSha256, ComputeSha256(source), StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException($"Rewrite-plan source SHA-256 does not match: {plan.RelativePath}");
    }

    var text = Encoding.UTF8.GetString(source);
    var lastStart = text.Length + 1;
    foreach (var edit in plan.Edits.OrderByDescending(edit => edit.Start))
    {
      if (edit.Start < 0 || edit.Length < 0 || edit.Start + edit.Length > text.Length ||
          edit.Start + edit.Length > lastStart ||
          !string.Equals(text.Substring(edit.Start, edit.Length), edit.OriginalText, StringComparison.Ordinal))
      {
        throw new InvalidOperationException($"Rewrite-plan edit is invalid: {plan.RelativePath}");
      }

      lastStart = edit.Start;
    }
  }
}
