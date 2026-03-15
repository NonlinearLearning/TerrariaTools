using System.Text.Json;
using TerrariaTools.Dome.Core;

namespace TerrariaTools.Dome.Cli;

/// <summary>
/// Dome CLI 配置记录。
/// </summary>
/// <param name="Command">命令名称。</param>
/// <param name="InputPath">输入路径。</param>
/// <param name="OutputPath">输出路径。</param>
/// <param name="RuleSet">规则集列表。</param>
/// <param name="LogLevel">日志级别。</param>
public sealed record DomeCliRunConfiguration(
    string? Command,
    string? InputPath,
    string? OutputPath,
    IReadOnlyList<string>? RuleSet,
    string? LogLevel,
    string? Loader,
    bool? AllowFallbackToSourceOnly);

/// <summary>
/// Dome CLI 解析结果记录。
/// </summary>
/// <param name="IsSuccess">是否解析成功。</param>
/// <param name="Request">运行请求。</param>
/// <param name="ErrorMessage">错误信息。</param>
public sealed record DomeCliCommandParseResult(
    bool IsSuccess,
    RunRequest? Request,
    TerrariaRuntimeRunRequest? TerrariaRuntimeRunRequest,
    TerrariaRuntimeShadowExtractionRequest? TerrariaRuntimeShadowExtractionRequest,
    string? ErrorMessage);

/// <summary>
/// Dome CLI 解析器，负责解析命令行参数。
/// </summary>
public static class DomeCliParser
{
    /// <summary>
    /// 使用说明文本。
    /// </summary>
    public const string UsageText = """
        Usage:
          dome run <input-path> <output-path>
          dome analyze <input-path> <output-path>
          dome plan <input-path> <output-path>
          dome tr-run
          dome tr-shadow
          dome --config <config-path>
        """;

    private const string TrSolutionPath = @"D:\lodes\TR\Backup\New1.27\1.45\TR\TerrariaServer.sln";
    private const string TrOutputRootPath = @"D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-runtime";
    private const string TrShadowOutputRootPath = @"D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-shadow";
    private const string TrShadowSeedMemberName = "Terraria.Main.DedServ";

    /// <summary>
    /// 异步解析命令行参数。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果。</returns>
    public static async Task<DomeCliCommandParseResult> ParseAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            return new DomeCliCommandParseResult(false, null, null, null, UsageText);
        }

        if (args.Length == 2 && string.Equals(args[0], "--config", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseConfigFileAsync(args[1], cancellationToken);
        }

        if (args.Length == 1 && string.Equals(args[0], "tr-run", StringComparison.OrdinalIgnoreCase))
        {
            return new DomeCliCommandParseResult(
                true,
                null,
                new TerrariaRuntimeRunRequest(TrSolutionPath, TrOutputRootPath),
                null,
                null);
        }

        if (args.Length == 1 && string.Equals(args[0], "tr-shadow", StringComparison.OrdinalIgnoreCase))
        {
            return new DomeCliCommandParseResult(
                true,
                null,
                null,
                new TerrariaRuntimeShadowExtractionRequest(TrSolutionPath, TrShadowOutputRootPath, TrShadowSeedMemberName),
                null);
        }

        if (args.Length < 3)
        {
            return new DomeCliCommandParseResult(false, null, null, null, UsageText);
        }

        if (!TryParseMode(args[0], out var mode))
        {
            return new DomeCliCommandParseResult(false, null, null, null, $"Unknown command '{args[0]}'.{Environment.NewLine}{UsageText}");
        }

        var optionsResult = ParseWorkspaceLoadOptions(args.Skip(3).ToArray());
        if (!optionsResult.IsSuccess)
        {
            return new DomeCliCommandParseResult(false, null, null, null, optionsResult.ErrorMessage);
        }

        return new DomeCliCommandParseResult(
            true,
            new RunRequest(args[1], args[2], Array.Empty<string>(), mode, optionsResult.Options!),
            null,
            null,
            null);
    }

    /// <summary>
    /// 异步解析配置文件。
    /// </summary>
    /// <param name="configPath">配置文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果。</returns>
    internal static DomeCliCommandParseResult ParseConfigJson(string json)
    {
        DomeCliRunConfiguration? config;
        try
        {
            config = JsonSerializer.Deserialize<DomeCliRunConfiguration>(json);
        }
        catch (JsonException ex)
        {
            return new DomeCliCommandParseResult(false, null, null, null, $"Config file contains invalid JSON: {ex.Message}");
        }

        return ParseConfig(config);
    }

    private static async Task<DomeCliCommandParseResult> ParseConfigFileAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new DomeCliCommandParseResult(false, null, null, null, $"Config file '{configPath}' was not found.");
        }

        var json = await File.ReadAllTextAsync(configPath, cancellationToken);
        return ParseConfigJson(json);
    }

    private static DomeCliCommandParseResult ParseConfig(DomeCliRunConfiguration? config)
    {
        if (config?.InputPath == null || config.OutputPath == null)
        {
            return new DomeCliCommandParseResult(false, null, null, null, "Config file must specify InputPath and OutputPath.");
        }

        var command = config.Command ?? "run";
        if (!TryParseMode(command, out var mode))
        {
            return new DomeCliCommandParseResult(false, null, null, null, $"Unknown config command '{command}'.");
        }

        if (!string.IsNullOrWhiteSpace(config.Loader) &&
            !TryParseLoaderPreference(config.Loader, out _))
        {
            return new DomeCliCommandParseResult(false, null, null, null, $"Unknown config loader '{config.Loader}'.");
        }

        return new DomeCliCommandParseResult(
            true,
            new RunRequest(
                config.InputPath,
                config.OutputPath,
                config.RuleSet ?? Array.Empty<string>(),
                mode,
                BuildWorkspaceLoadOptions(config.Loader, config.AllowFallbackToSourceOnly)),
            null,
            null,
            null);
    }

    /// <summary>
    /// 解析工作区加载选项。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <returns>解析结果（是否成功、选项、错误信息）。</returns>
    private static (bool IsSuccess, WorkspaceLoadOptions? Options, string? ErrorMessage) ParseWorkspaceLoadOptions(string[] args)
    {
        var preferredLoader = WorkspaceLoaderPreference.Auto;
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

        return (true, new WorkspaceLoadOptions(preferredLoader, allowFallback), null);
    }

    /// <summary>
    /// 构建工作区加载选项。
    /// </summary>
    /// <param name="loader">加载器偏好字符串。</param>
    /// <param name="allowFallback">是否允许回退。</param>
    /// <returns>工作区加载选项。</returns>
    private static WorkspaceLoadOptions BuildWorkspaceLoadOptions(string? loader, bool? allowFallback)
    {
        var preferredLoader = WorkspaceLoaderPreference.Auto;
        if (!string.IsNullOrWhiteSpace(loader) && TryParseLoaderPreference(loader, out var parsedPreference))
        {
            preferredLoader = parsedPreference;
        }

        return new WorkspaceLoadOptions(preferredLoader, allowFallback ?? true);
    }

    /// <summary>
    /// 尝试解析运行模式。
    /// </summary>
    /// <param name="command">命令字符串。</param>
    /// <param name="mode">输出的运行模式。</param>
    /// <returns>如果解析成功则返回 true，否则返回 false。</returns>
    private static bool TryParseMode(string command, out RunMode mode)
    {
        var normalized = command.ToLowerInvariant();
        mode = normalized switch
        {
            "run" => RunMode.Standard,
            "analyze" => RunMode.AnalyzeOnly,
            "plan" => RunMode.PlanOnly,
            _ => RunMode.Standard
        };

        return normalized is "run" or "analyze" or "plan";
    }

    /// <summary>
    /// 尝试解析加载器偏好。
    /// </summary>
    /// <param name="value">输入字符串。</param>
    /// <param name="preference">输出的加载器偏好。</param>
    /// <returns>如果解析成功则返回 true，否则返回 false。</returns>
    private static bool TryParseLoaderPreference(string value, out WorkspaceLoaderPreference preference)
    {
        var normalized = value.ToLowerInvariant();
        preference = normalized switch
        {
            "auto" => WorkspaceLoaderPreference.Auto,
            "codeanalysis" => WorkspaceLoaderPreference.CodeAnalysisFirst,
            "sourceonly" => WorkspaceLoaderPreference.SourceOnly,
            _ => WorkspaceLoaderPreference.Auto
        };

        return normalized is "auto" or "codeanalysis" or "sourceonly";
    }
}
