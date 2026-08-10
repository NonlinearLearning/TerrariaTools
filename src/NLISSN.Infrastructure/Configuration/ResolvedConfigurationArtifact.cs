using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NLISSN.Infrastructure.Workspace;

namespace NLISSN.Infrastructure.Configuration;

internal sealed record ResolvedConfigurationArtifact(
    int SchemaVersion,
    string InputPath,
    RulePolicySettings RulePolicy,
    ExecutionSettings Execution,
    ArtifactSettings Artifacts,
    LoggingSettings Logging,
    ConfigurationProvenance Provenance,
    ResolvedWorkspaceInput? Workspace,
    string Fingerprint)
{
    internal static ResolvedConfigurationArtifact Create(AnalysisConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var projection = new
        {
            schemaVersion = configuration.Provenance.TargetSchemaVersion,
            inputPath = configuration.InputPath,
            rulePolicy = new
            {
                configuration.RulePolicy.TargetName,
                configuration.RulePolicy.DeleteClass,
                disabledRuleTypes = configuration.RulePolicy.DisabledRuleTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                configuration.RulePolicy.ValidateBindings,
                configuration.RulePolicy.DeleteUnreachableMethods,
                configuration.RulePolicy.DeleteUnreferencedMethods,
                configuration.RulePolicy.ClearUnusedInterfaceImplementations,
                configuration.RulePolicy.PrivatizeInternalOnlyPublicMethods
            },
            execution = configuration.Execution,
            artifacts = new
            {
                configuration.Artifacts.WriteDiff,
                configuration.Artifacts.WriteRuntimeLog,
                configuration.Artifacts.WriteEvidence,
                configuration.Artifacts.RewritePlanMode,
              configuration.Artifacts.DiffView
            },
            workspace = configuration.Workspace is null
              ? null
              : new
              {
                  configuration.Workspace.Path,
                  configuration.Workspace.ProjectPath,
                  configuration.Workspace.TargetFramework,
                  configuration.Workspace.Configuration,
                  configuration.Workspace.Platform,
                  configuration.Workspace.TargetDocumentPath,
                  restore = configuration.Workspace.RestoreMode.ToString(),
                  generatedSources = configuration.Workspace.GeneratedSourceMode.ToString(),
                  generators = configuration.Workspace.GeneratorMode.ToString()
              },
            logging = new
            {
                configuration.Logging.Profile,
                configuration.Logging.Level,
                categories = configuration.Logging.Categories.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                events = configuration.Logging.Events.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                configuration.Logging.View
            },
            provenance = new
            {
                configuration.Provenance.SourceSchemaVersion,
                configuration.Provenance.TargetSchemaVersion,
                appliedMigrations = configuration.Provenance.AppliedMigrations.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                fieldOrigins = configuration.Provenance.FieldOrigins.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                  .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
            }
        };
        var canonical = JsonSerializer.Serialize(projection);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new ResolvedConfigurationArtifact(
            configuration.Provenance.TargetSchemaVersion,
            configuration.InputPath,
            configuration.RulePolicy,
            configuration.Execution,
            configuration.Artifacts,
            configuration.Logging,
            configuration.Provenance,
            ToResolvedWorkspace(configuration.Workspace),
            fingerprint);
    }

    private static ResolvedWorkspaceInput? ToResolvedWorkspace(WorkspaceInputOptions? workspace)
    {
        return workspace is null
          ? null
          : new ResolvedWorkspaceInput(
            workspace.Path,
            workspace.ProjectPath,
            workspace.TargetFramework,
            workspace.Configuration,
            workspace.Platform,
            workspace.TargetDocumentPath,
            workspace.RestoreMode.ToString(),
            workspace.GeneratedSourceMode.ToString(),
            workspace.GeneratorMode.ToString());
    }
}

internal sealed record ResolvedWorkspaceInput(
    string Path,
    string? ProjectPath,
    string? TargetFramework,
    string Configuration,
    string Platform,
    string? TargetDocumentPath,
    string Restore,
    string GeneratedSources,
    string Generators);
