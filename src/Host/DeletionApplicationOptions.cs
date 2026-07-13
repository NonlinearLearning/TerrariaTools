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

    internal static bool ShouldWriteDiff(IReadOnlyDictionary<string, string> options)
    {
        return !IsTrueOption(options, "no-diff") &&
          !IsTrueOption(options, "skip-diff");
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
