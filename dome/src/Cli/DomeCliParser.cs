using System.Text.Json;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Cli;

public sealed record DomeCliRunConfiguration(
    string? Command,
    string? InputPath,
    string? OutputPath,
    IReadOnlyList<string>? RuleSet,
    string? LogLevel,
    string? Loader,
    bool? AllowFallbackToSourceOnly);

public sealed record DomeCliCommandParseResult(
    bool IsSuccess,
    ApplicationAbstractions.RunRequest? Request,
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

    public static async Task<DomeCliCommandParseResult> ParseAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            return new DomeCliCommandParseResult(false, null, UsageText);
        }

        if (args.Length == 2 && string.Equals(args[0], "--config", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseConfigFileAsync(args[1], cancellationToken);
        }

        if (args.Length == 1 &&
            (string.Equals(args[0], "tr-run", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "tr-shadow", StringComparison.OrdinalIgnoreCase)))
        {
            return new DomeCliCommandParseResult(
                false,
                null,
                "Legacy runtime commands are no longer available from the standard CLI.");
        }

        if (args.Length < 3)
        {
            return new DomeCliCommandParseResult(false, null, UsageText);
        }

        if (!TryParseMode(args[0], out var mode))
        {
            return new DomeCliCommandParseResult(false, null, $"Unknown command '{args[0]}'.{Environment.NewLine}{UsageText}");
        }

        var optionsResult = ParseWorkspaceLoadOptions(args.Skip(3).ToArray());
        if (!optionsResult.IsSuccess)
        {
            return new DomeCliCommandParseResult(false, null, optionsResult.ErrorMessage);
        }

        return new DomeCliCommandParseResult(
            true,
            new ApplicationAbstractions.RunRequest(args[1], args[2], Array.Empty<string>(), mode, optionsResult.Options!),
            null);
    }

    internal static DomeCliCommandParseResult ParseConfigJson(string json)
    {
        DomeCliRunConfiguration? config;
        try
        {
            config = JsonSerializer.Deserialize<DomeCliRunConfiguration>(json);
        }
        catch (JsonException ex)
        {
            return new DomeCliCommandParseResult(false, null, $"Config file contains invalid JSON: {ex.Message}");
        }

        return ParseConfig(config);
    }

    private static async Task<DomeCliCommandParseResult> ParseConfigFileAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new DomeCliCommandParseResult(false, null, $"Config file '{configPath}' was not found.");
        }

        var json = await File.ReadAllTextAsync(configPath, cancellationToken);
        return ParseConfigJson(json);
    }

    private static DomeCliCommandParseResult ParseConfig(DomeCliRunConfiguration? config)
    {
        if (config?.InputPath == null || config.OutputPath == null)
        {
            return new DomeCliCommandParseResult(false, null, "Config file must specify InputPath and OutputPath.");
        }

        var command = config.Command ?? "run";
        if (!TryParseMode(command, out var mode))
        {
            return new DomeCliCommandParseResult(false, null, $"Unknown config command '{command}'.");
        }

        if (!string.IsNullOrWhiteSpace(config.Loader) &&
            !TryParseLoaderPreference(config.Loader, out _))
        {
            return new DomeCliCommandParseResult(false, null, $"Unknown config loader '{config.Loader}'.");
        }

        return new DomeCliCommandParseResult(
            true,
            new ApplicationAbstractions.RunRequest(
                config.InputPath,
                config.OutputPath,
                config.RuleSet ?? Array.Empty<string>(),
                mode,
                BuildWorkspaceLoadOptions(config.Loader, config.AllowFallbackToSourceOnly)),
            null);
    }

    private static (bool IsSuccess, ApplicationAbstractions.WorkspaceLoadOptions? Options, string? ErrorMessage) ParseWorkspaceLoadOptions(string[] args)
    {
        var preferredLoader = ApplicationAbstractions.WorkspaceLoaderPreference.Auto;
        var allowFallback = true;

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--no-fallback", StringComparison.OrdinalIgnoreCase))
            {
                allowFallback = false;
                continue;
            }

            if (string.Equals(args[index], "--loader", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return (false, null, "Option '--loader' requires a value.");
                }

                if (!TryParseLoaderPreference(args[index + 1], out preferredLoader))
                {
                    return (false, null, $"Unknown loader '{args[index + 1]}'.");
                }

                index++;
                continue;
            }

            return (false, null, $"Unknown option '{args[index]}'.");
        }

        return (true, new ApplicationAbstractions.WorkspaceLoadOptions(preferredLoader, allowFallback), null);
    }

    private static ApplicationAbstractions.WorkspaceLoadOptions BuildWorkspaceLoadOptions(string? loader, bool? allowFallback)
    {
        var preferredLoader = ApplicationAbstractions.WorkspaceLoaderPreference.Auto;
        if (!string.IsNullOrWhiteSpace(loader) && TryParseLoaderPreference(loader, out var parsedPreference))
        {
            preferredLoader = parsedPreference;
        }

        return new ApplicationAbstractions.WorkspaceLoadOptions(preferredLoader, allowFallback ?? true);
    }

    private static bool TryParseMode(string command, out ModelPrimitives.RunMode mode)
    {
        var normalized = command.ToLowerInvariant();
        mode = normalized switch
        {
            "run" => ModelPrimitives.RunMode.Standard,
            "analyze" => ModelPrimitives.RunMode.AnalyzeOnly,
            "plan" => ModelPrimitives.RunMode.PlanOnly,
            _ => ModelPrimitives.RunMode.Standard
        };

        return normalized is "run" or "analyze" or "plan";
    }

    private static bool TryParseLoaderPreference(string value, out ApplicationAbstractions.WorkspaceLoaderPreference preference)
    {
        var normalized = value.ToLowerInvariant();
        preference = normalized switch
        {
            "auto" => ApplicationAbstractions.WorkspaceLoaderPreference.Auto,
            "codeanalysis" => ApplicationAbstractions.WorkspaceLoaderPreference.CodeAnalysisFirst,
            "sourceonly" => ApplicationAbstractions.WorkspaceLoaderPreference.SourceOnly,
            _ => ApplicationAbstractions.WorkspaceLoaderPreference.Auto
        };

        return normalized is "auto" or "codeanalysis" or "sourceonly";
    }
}
