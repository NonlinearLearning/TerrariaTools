using System.Text.Json;
using TerrariaTools.Dome.Core;

namespace TerrariaTools.Dome.Cli;

public sealed record DomeCliConfiguration(
    string? Command,
    string? InputPath,
    string? OutputPath,
    IReadOnlyList<string>? RuleSet,
    string? LogLevel);

public sealed record DomeCliParseResult(
    bool IsSuccess,
    RunRequest? Request,
    string? ErrorMessage);

public static class DomeCliParser
{
    public const string UsageText = """
        Usage:
          dome run <input-path> <output-path>
          dome analyze <input-path> <output-path>
          dome plan <input-path> <output-path>
          dome --config <config-path>
        """;

    public static async Task<DomeCliParseResult> ParseAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            return new DomeCliParseResult(false, null, UsageText);
        }

        if (args.Length == 2 && string.Equals(args[0], "--config", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseConfigAsync(args[1], cancellationToken);
        }

        if (args.Length != 3)
        {
            return new DomeCliParseResult(false, null, UsageText);
        }

        if (!TryParseMode(args[0], out var mode))
        {
            return new DomeCliParseResult(false, null, $"Unknown command '{args[0]}'.{Environment.NewLine}{UsageText}");
        }

        return new DomeCliParseResult(
            true,
            new RunRequest(args[1], args[2], Array.Empty<string>(), mode),
            null);
    }

    private static async Task<DomeCliParseResult> ParseConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new DomeCliParseResult(false, null, $"Config file '{configPath}' was not found.");
        }

        var json = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<DomeCliConfiguration>(json);
        if (config?.InputPath == null || config.OutputPath == null)
        {
            return new DomeCliParseResult(false, null, "Config file must specify InputPath and OutputPath.");
        }

        var command = config.Command ?? "run";
        if (!TryParseMode(command, out var mode))
        {
            return new DomeCliParseResult(false, null, $"Unknown config command '{command}'.");
        }

        return new DomeCliParseResult(
            true,
            new RunRequest(
                config.InputPath,
                config.OutputPath,
                config.RuleSet ?? Array.Empty<string>(),
                mode),
            null);
    }

    private static bool TryParseMode(string command, out RunMode mode)
    {
        mode = command.ToLowerInvariant() switch
        {
            "run" => RunMode.Standard,
            "analyze" => RunMode.AnalyzeOnly,
            "plan" => RunMode.PlanOnly,
            _ => RunMode.Standard
        };

        return command is "run" or "analyze" or "plan";
    }
}
