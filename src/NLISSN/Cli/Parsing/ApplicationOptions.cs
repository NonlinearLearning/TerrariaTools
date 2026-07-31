using NLISSN.Application;
using NLISSN.Core.Pipeline;

namespace NLISSN.Cli.Parsing;

/// 解析命令行开关，并集中处理其派生的执行决策。
internal static class  ApplicationOptions
{
    /// 将支持的 <c>--key value</c> 和仅含开关的参数转换为不区分大小写的映射。
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

    internal static bool TryParseDisabledRuleTypes(IReadOnlyDictionary<string, string> options, out IReadOnlyList<string> disabledRuleTypes)
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

    internal static string? ResolveRuntimeLogPath(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRequiredFilePathOption(options, "runtime-log") ??
          ResolveRequiredFilePathOption(options, "runtime-metrics-log");
    }

    internal static string? ResolveRewritePlanOutPath(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRequiredPathOption(options, "rewrite-plan-out");
    }

    internal static string? ResolveRewritePlanInPath(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRequiredPathOption(options, "rewrite-plan-in");
    }

    internal static string? ResolveEvidenceJsonPath(IReadOnlyDictionary<string, string> options)
    {
        return ResolveRequiredFilePathOption(options, "evidence-json");
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

    private static string? ResolveRequiredFilePathOption(
        IReadOnlyDictionary<string, string> options,
        string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"--{key} requires a non-empty file path.");
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

    internal static  AnalysisRuntime CreateRuntime(IReadOnlyDictionary<string, string> options)
    {
        return AnalysisRuntimeFactory.CreateFromOptions(options);
    }
}
