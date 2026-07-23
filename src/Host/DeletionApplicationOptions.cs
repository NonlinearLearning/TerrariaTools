using RoslynPrototype.Application.Logging;
using Rules;

namespace RoslynPrototype.Application;

internal static class DeletionApplicationOptions
{
    internal static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[arg[2..]] = "true";
                continue;
            }

            options[arg[2..]] = args[index + 1];
            index++;
        }

        return options;
    }

    internal static bool TryParseDisabledRuleTypes(
      IReadOnlyDictionary<string, string> options,
      out IReadOnlyList<string> disabledRuleTypes)
    {
        disabledRuleTypes = Array.Empty<string>();
        if (!options.TryGetValue("disabled-rule-types", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        disabledRuleTypes = rawValue
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToList();
        return disabledRuleTypes.Count > 0;
    }

    internal static bool ShouldWriteBack(IReadOnlyDictionary<string, string> options)
    {
        return options.TryGetValue("write-back", out var rawValue) &&
          string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ResolveRewritePlanOutPath(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRequiredPathOption(options, "rewrite-plan-out");
    }

    internal static string? ResolveRewritePlanInPath(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRequiredPathOption(options, "rewrite-plan-in");
    }

    internal static void ValidateRewritePlanOptions(IReadOnlyDictionary<string, string> options)
    {
        var outputPath = ResolveRewritePlanOutPath(options);
        var inputPath = ResolveRewritePlanInPath(options);
        if (outputPath is not null && inputPath is not null)
        {
            throw new ArgumentException("--rewrite-plan-out and --rewrite-plan-in cannot be combined.");
        }

        if (inputPath is not null &&
            (options.ContainsKey("delete-class") || options.ContainsKey("target-name") || IsTrueOption(options, "skip-rewrite")))
        {
            throw new ArgumentException("--rewrite-plan-in cannot be combined with analysis-driving options.");
        }
    }

    private static string? ResolveRequiredPathOption(IReadOnlyDictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"--{key} requires a non-empty directory path.");
        }

        return value;
    }

    internal static bool ShouldWriteDiff(IReadOnlyDictionary<string, string> options)
    {
        return !IsTrueOption(options, "no-diff") &&
          !IsTrueOption(options, "skip-diff");
    }

    internal static string ResolveDiffView(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("diff-view", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return "legacy";
        }

        return string.Equals(rawValue, "readable", StringComparison.OrdinalIgnoreCase)
          ? "readable"
          : "legacy";
    }

    internal static bool IsTrueOption(IReadOnlyDictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out var rawValue) &&
          string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldUseUnreferencedMethodFastPath(
      IReadOnlyDictionary<string, string> options)
    {
        return IsTrueOption(options, "delete-unreferenced-methods") &&
          !options.ContainsKey("target-name") &&
          !options.ContainsKey("delete-class") &&
          !options.ContainsKey("unreachable-methods") &&
          !IsTrueOption(options, "clear-unused-interface-implementations") &&
          !IsTrueOption(options, "privatize-internal-only-public-methods");
    }

    internal static bool ShouldUseDeleteClassUsingCleanup(
      IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          !IsTrueOption(options, "fast-delete-class-directory");
    }

    internal static bool ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(
      IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          IsTrueOption(options, "fast-delete-class-directory");
    }

    internal static bool ShouldFilterDeleteClassFilesByTargetName(
      IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          IsTrueOption(options, "fast-delete-class-directory") &&
          IsTrueOption(options, "filter-delete-class-files-by-target-name");
    }

    internal static bool HasAnyTextLogOptions(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRuntimeLogPath(options) is not null ||
          ResolveAnalysisLogPath(options) is not null ||
          options.ContainsKey("log-level") ||
          options.ContainsKey("log-categories") ||
          options.ContainsKey("log-events") ||
          options.ContainsKey("log-view") ||
          options.ContainsKey("log-profile");
    }

    internal static string? ResolveRuntimeLogPath(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("runtime-log", out var runtimeLogPath) &&
            !string.IsNullOrWhiteSpace(runtimeLogPath))
        {
            return runtimeLogPath;
        }

        if (options.TryGetValue("runtime-metrics-log", out var legacyRuntimeLogPath) &&
            !string.IsNullOrWhiteSpace(legacyRuntimeLogPath))
        {
            return legacyRuntimeLogPath;
        }

        return null;
    }

    internal static string? ResolveAnalysisLogPath(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("analysis-log", out var analysisLogPath) &&
            !string.IsNullOrWhiteSpace(analysisLogPath))
        {
            return analysisLogPath;
        }

        if (options.TryGetValue("analysis-events-log", out var analysisEventsLogPath) &&
            !string.IsNullOrWhiteSpace(analysisEventsLogPath))
        {
            return analysisEventsLogPath;
        }

        if (options.TryGetValue("per-file-timing-log", out var perFileTimingLogPath) &&
            !string.IsNullOrWhiteSpace(perFileTimingLogPath))
        {
            return perFileTimingLogPath;
        }

        if (options.TryGetValue("per-file-memory-diagnostics-log", out var perFileMemoryDiagnosticsLogPath) &&
            !string.IsNullOrWhiteSpace(perFileMemoryDiagnosticsLogPath))
        {
            return perFileMemoryDiagnosticsLogPath;
        }

        if (options.TryGetValue("per-file-phase-timing-log-directory", out var legacyDirectoryPath) &&
            !string.IsNullOrWhiteSpace(legacyDirectoryPath))
        {
            return Path.Combine(legacyDirectoryPath, "analysis.log");
        }

        return null;
    }

    internal static int ResolveMaxDegreeOfParallelism(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("max-degree-of-parallelism", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return Math.Max(1, Environment.ProcessorCount);
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            return Math.Max(1, Environment.ProcessorCount);
        }

        return Math.Max(1, parsedValue);
    }

    internal static RoslynPrototypeExecutionOptions CreateExecutionOptions(
      IReadOnlyDictionary<string, string> options)
    {
        return DeletionAnalysisRuntime.CreateExecutionOptions(options);
    }

    internal static DeletionAnalysisRuntime CreateRuntime(
      IReadOnlyDictionary<string, string> options)
    {
        return DeletionAnalysisRuntime.CreateFromOptions(options);
    }
}
