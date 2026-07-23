using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPrototype.Rewrite;

/// <summary>
/// A portable, text-only rewrite operation. It intentionally contains no Roslyn objects.
/// </summary>
public sealed record RewritePlanEdit(
  int Start,
  int Length,
  string OriginalText,
  string ReplacementText)
{
  [JsonIgnore]
  public TextSpan Span => new(Start, Length);
}

/// <summary>
/// The rewrite operations for one source file, relative to an artifact input root.
/// </summary>
public sealed record RewritePlanFile(
  string RelativePath,
  string SourceSha256,
  IReadOnlyList<RewritePlanEdit> Edits);

/// <summary>
/// The versioned project-level metadata for a rewrite-plan artifact.
/// </summary>
public sealed record RewritePlanManifest(
  int SchemaVersion,
  string Operation,
  string InputRoot,
  int SourceFileCount,
  int PlannedFileCount,
  string PlanFile,
  string PlanSha256);
