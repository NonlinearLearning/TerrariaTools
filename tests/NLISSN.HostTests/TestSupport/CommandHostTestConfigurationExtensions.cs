using NLISSN.Infrastructure.Configuration;
using NLISSN.Application;
using NLISSN.Core.Rewrite;

namespace NLISSN.Hosting;

internal static class CommandHostTestConfigurationExtensions
{
  // Temporary input syntax keeps focused rule tests independent from process startup.
  internal static PrototypeAnalysisResult AnalyzeFromArgs(this CommandHost host, string[] arguments)
  {
    ArgumentNullException.ThrowIfNull(host);
    ArgumentNullException.ThrowIfNull(arguments);

    string? inputPath = null;
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index++)
    {
      var argument = arguments[index];
      if (!argument.StartsWith("--", StringComparison.Ordinal))
      {
        inputPath ??= argument;
        continue;
      }

      var key = argument[2..];
      var value = index + 1 < arguments.Length &&
        !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
        ? arguments[++index]
        : "true";
      options[key] = value;
    }

    var hasExplicitInputPath = inputPath is not null;
    var resolvedInputPath = inputPath ?? "demo.cs";
    var result = host.Analyze(CreateConfiguration(resolvedInputPath, options));
    return MaterializeLegacyDiffFile(result, resolvedInputPath, hasExplicitInputPath, options);
  }

  internal static Task<PrototypeAnalysisResult> AnalyzeFromArgsAsync(
    this CommandHost host,
    string[] arguments)
  {
    return Task.FromResult(host.AnalyzeFromArgs(arguments));
  }

  private static AnalysisConfiguration CreateConfiguration(
    string inputPath,
    IReadOnlyDictionary<string, string> options)
  {
    return new AnalysisConfiguration(
      inputPath,
      new RulePolicySettings(
        GetValue(options, "target-name"),
        GetValue(options, "delete-class"),
        GetValues(options, "disabled-rule-types"),
        IsTrue(options, "validate-bindings"),
        IsTrue(options, "delete-unreachable-methods"),
        IsTrue(options, "delete-unreferenced-methods"),
        IsTrue(options, "clear-unused-interface-implementations"),
        IsTrue(options, "privatize-internal-only-public-methods")),
      new ExecutionSettings(
        IsTrue(options, "write-back"),
        IsTrue(options, "skip-rewrite"),
        GetConcurrencyValue(options, "directory-max-degree-of-parallelism"),
        GetConcurrencyValue(options, "cpg-max-degree-of-parallelism"),
        GetConcurrencyValue(options, "group-max-degree-of-parallelism"),
        GetConcurrencyValue(options, "helper-max-degree-of-parallelism"),
        GetConcurrencyValue(options, "replay-max-degree-of-parallelism"),
        GetConcurrencyValue(options, "max-concurrent-operations"),
        !IsTrue(options, "disable-directory-parallelism"),
        IsTrue(options, "enable-group-parallelism"),
        !IsTrue(options, "disable-helper-parallelism"),
        IsTrue(options, "fast-delete-class-directory"),
        IsTrue(options, "filter-delete-class-files-by-target-name")),
      new ArtifactSettings(
        string.Empty,
        GetValue(options, "diff-out") ?? GetValue(options, "diff-root") ?? Path.Combine(Path.GetFullPath(Path.Combine("Build", "Result")), "Diff"),
        GetValue(options, "runtime-log") ?? string.Empty,
        GetValue(options, "evidence-json") ?? string.Empty,
        GetValue(options, "rewrite-plan-out") ?? string.Empty,
        GetValue(options, "rewrite-plan-in"),
        string.Empty,
        !IsTrue(options, "no-diff") && !IsTrue(options, "skip-diff"),
        options.ContainsKey("runtime-log"),
        options.ContainsKey("evidence-json"),
        options.ContainsKey("rewrite-plan-in") ? RewritePlanMode.Replay :
          options.ContainsKey("rewrite-plan-out") ? RewritePlanMode.Capture : RewritePlanMode.None,
        GetValue(options, "diff-view") ?? "legacy"),
      new LoggingSettings(
        GetValue(options, "log-profile") ?? "normal",
        GetValue(options, "log-level") ?? "debug",
        GetValues(options, "log-categories").ToArray(),
        GetValues(options, "log-events").ToArray(),
        GetValue(options, "log-view") ??
          (string.Equals(GetValue(options, "log-profile"), "benchmark", StringComparison.OrdinalIgnoreCase)
            ? "benchmark"
            : "normal")),
      new ConfigurationProvenance(2, 2, Array.Empty<string>(), new Dictionary<string, string>()));
  }

  private static PrototypeAnalysisResult MaterializeLegacyDiffFile(
    PrototypeAnalysisResult result,
    string inputPath,
    bool hasExplicitInputPath,
    IReadOnlyDictionary<string, string> options)
  {
    if (!hasExplicitInputPath ||
        Directory.Exists(inputPath) ||
        result.Diff.Summary.EditCount == 0 ||
        IsTrue(options, "no-diff") ||
        IsTrue(options, "skip-diff"))
    {
      return result;
    }

    var diffPath = GetValue(options, "diff-out") ?? GetDefaultDiffPath(inputPath);
    var fullDiffPath = Path.GetFullPath(diffPath);
    if (Directory.Exists(fullDiffPath))
    {
      Directory.Delete(fullDiffPath, recursive: true);
    }

    var parentDirectory = Path.GetDirectoryName(fullDiffPath);
    if (!string.IsNullOrWhiteSpace(parentDirectory))
    {
      Directory.CreateDirectory(parentDirectory);
    }

    var view = GetValue(options, "diff-view") ?? "legacy";
    File.WriteAllText(fullDiffPath, new TextDiffRenderer().Render(result.Diff, view));
    return result with { DiffFilePath = fullDiffPath };
  }

  private static string GetDefaultDiffPath(string inputPath)
  {
    var fileName = Path.GetFileNameWithoutExtension(inputPath);
    return Path.Combine(
      Directory.GetCurrentDirectory(),
      "Build",
      "Result",
      "Diff",
      $"{fileName}.rewrite.diff");
  }

  private static bool IsTrue(IReadOnlyDictionary<string, string> options, string key)
  {
    return string.Equals(GetValue(options, key), "true", StringComparison.OrdinalIgnoreCase);
  }

  private static string? GetValue(IReadOnlyDictionary<string, string> options, string key)
  {
    return options.TryGetValue(key, out var value) ? value : null;
  }

  private static HashSet<string> GetValues(IReadOnlyDictionary<string, string> options, string key)
  {
    return (GetValue(options, key) ?? string.Empty)
      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  private static int GetConcurrencyValue(
    IReadOnlyDictionary<string, string> options,
    string key)
  {
    var defaultDegree = GetValue(options, "max-degree-of-parallelism");
    var rawValue = GetValue(options, key) ?? defaultDegree;
    if (rawValue is null)
    {
      return Math.Max(1, Environment.ProcessorCount);
    }

    if (!int.TryParse(rawValue, out var value) || value <= 0)
    {
      throw new ArgumentException($"--{key} must be a positive integer.", key);
    }

    return value;
  }
}
