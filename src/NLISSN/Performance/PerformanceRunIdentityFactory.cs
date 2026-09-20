using System.Security.Cryptography;
using System.Text;
using NLCPG.Contracts;
using NLISSN.Application;
using NLISSN.Core.Performance;
using NLISSN.Core.Pipeline;
using NLISSN.Infrastructure.Configuration;

namespace NLISSN.Performance;

/// Builds the comparison identity only after the real run has produced its facts.
/// The factory owns no analysis state and only hashes stable projections.
internal static class PerformanceRunIdentityFactory
{
  public static PerformanceRunIdentity Create(
    AnalysisConfiguration configuration,
    RulePipeline pipeline,
    AnalysisRunOutcome outcome,
    AnalysisRuntime runtime,
    PerformanceMode mode)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(pipeline);
    ArgumentNullException.ThrowIfNull(outcome);
    ArgumentNullException.ThrowIfNull(runtime);

    var items = outcome.Performance.Items
      .OrderBy(item => item.ItemId, StringComparer.Ordinal)
      .ToArray();
    var sourceManifestHash = HashLines(items.Select(item =>
      $"{item.ItemId}|{item.Cpg?.SourceIdentity ?? "unknown"}"));
    var graphSnapshot = HashLines(items.Select(item =>
      $"{item.ItemId}|{item.Cpg?.NodeCount?.ToString() ?? "unknown"}|{item.Cpg?.EdgeCount?.ToString() ?? "unknown"}"));
    var ruleSnapshot = HashLines(items.SelectMany(item =>
      (item.RuleGraph?.NodeSamples ?? Array.Empty<RuleGraphNodePerformanceFact>())
        .OrderBy(node => node.NodeId, StringComparer.Ordinal)
        .Select(node =>
          $"{item.ItemId}|{node.NodeId}|{node.InputCount?.ToString() ?? "unknown"}|{node.OutputCount?.ToString() ?? "unknown"}|{node.Status}")));
    var artifactSnapshot = HashLines(items.Select(item =>
      $"{item.ItemId}|{item.Rewrite?.EditCount?.ToString() ?? "unknown"}|{item.Rewrite?.DiffFileCount?.ToString() ?? "unknown"}|{item.Status}"));
    var ruleProfileHash = HashLines(GetRuleDescriptors(pipeline));
    var capabilityFingerprint = HashLines(
      pipeline.GetRequiredCapabilities().Select(capability => capability.ToString()));
    var sdk = Environment.GetEnvironmentVariable("DOTNET_SDK_VERSION") ?? Environment.Version.ToString();
    var runtimeVersion = Environment.Version.ToString();
    var operatingSystem = Environment.OSVersion.VersionString;
    var cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? Environment.MachineName;
    var environmentFingerprint = HashLines(new[] { sdk, runtimeVersion, operatingSystem, cpu });
    var configurationFingerprint = ResolvedConfigurationArtifact.Create(configuration).Fingerprint;

    return new PerformanceRunIdentity(
      NormalizeInputIdentity(outcome.Performance.InputIdentity),
      ruleProfileHash,
      capabilityFingerprint,
      "in-process",
      sdk,
      runtimeVersion,
      operatingSystem,
      cpu,
      environmentFingerprint,
      Math.Max(1, configuration.Execution.MaxDegreeOfParallelism),
      runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism,
      configuration.Execution.GroupParallelism
        ? Math.Max(1, configuration.Execution.MaxDegreeOfParallelism)
        : 1,
      mode,
      mode is PerformanceMode.Diagnostic or PerformanceMode.Profile,
      graphSnapshot,
      ruleSnapshot,
      artifactSnapshot,
      ResolveGitCommit(),
      sourceManifestHash,
      configurationFingerprint);
  }

  private static IEnumerable<string> GetRuleDescriptors(RulePipeline pipeline)
  {
    return pipeline.Markers.Select(rule => $"Mark|{rule.RuleId}|{Capabilities(rule.RequiredCapabilities)}")
      .Concat(pipeline.Propagators.Select(rule => $"Propagate|{rule.RuleId}|{Capabilities(rule.RequiredCapabilities)}"))
      .Concat(pipeline.Lifters.Select(rule => $"Lift|{rule.RuleId}|{Capabilities(rule.RequiredCapabilities)}"))
      .Concat(pipeline.Proposers.Select(rule => $"Propose|{rule.RuleId}|{Capabilities(rule.RequiredCapabilities)}"));
  }

  private static string Capabilities(IReadOnlyCollection<NLCPGCapability> capabilities)
  {
    return string.Join(',', capabilities.OrderBy(capability => capability));
  }

  private static string? ResolveGitCommit()
  {
    return new[] { "GIT_COMMIT", "SOURCE_VERSION", "BUILD_SOURCEVERSION" }
      .Select(Environment.GetEnvironmentVariable)
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
  }

  private static string? NormalizeInputIdentity(string? inputIdentity)
  {
    return string.IsNullOrWhiteSpace(inputIdentity)
      ? null
      : Path.GetFullPath(inputIdentity);
  }

  private static string HashLines(IEnumerable<string> values)
  {
    var canonical = string.Join('\n', values.OrderBy(value => value, StringComparer.Ordinal));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
  }
}
