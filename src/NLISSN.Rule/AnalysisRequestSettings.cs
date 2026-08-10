namespace NLISSN.Core.Pipeline;

/// <summary>
/// Immutable rule and rewrite settings consumed by the analysis core.
/// </summary>
public sealed record AnalysisRequestSettings(
  IReadOnlyList<string> TargetNames,
  IReadOnlyList<string> DeleteClassNames,
  bool SkipRewrite,
  bool ValidateBindings,
  bool DeleteUnreferencedMethods,
  bool ClearUnusedInterfaceImplementations,
  bool PrivatizeInternalOnlyPublicMethods,
  bool FastDeleteClassDirectory,
  bool FilterDeleteClassFilesByTargetName)
{
    public bool HasDeleteClass => DeleteClassNames.Count > 0;
}
