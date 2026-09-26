using NLISSN.Core.Pipeline;
using NLISSN.Infrastructure.Workspace;

namespace NLISSN.Infrastructure.Configuration;

internal sealed record AnalysisConfiguration(
  string InputPath,
  RulePolicySettings RulePolicy,
  ExecutionSettings Execution,
  ArtifactSettings Artifacts,
  LoggingSettings Logging,
  ConfigurationProvenance Provenance,
  WorkspaceInputOptions? Workspace = null,
  ProjectExportSettings? ProjectExport = null)
{
    internal AnalysisRequestSettings CreateAnalysisRequestSettings()
    {
        return new AnalysisRequestSettings(
          SplitNames(RulePolicy.TargetName),
          SplitNames(RulePolicy.DeleteClass),
          Execution.SkipRewrite,
          RulePolicy.ValidateBindings,
          RulePolicy.DeleteUnreferencedMethods,
          RulePolicy.ClearUnusedInterfaceImplementations,
          RulePolicy.PrivatizeInternalOnlyPublicMethods,
          Execution.FastDeleteClassDirectory,
          Execution.FilterDeleteClassFilesByTargetName);
    }

    private static IReadOnlyList<string> SplitNames(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
          ? Array.Empty<string>()
          : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

internal enum RewritePlanMode
{
    None,
    Capture,
    Replay
}
