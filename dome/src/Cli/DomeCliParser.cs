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
public sealed record DomeCliConfiguration(
    string? Command,
    string? InputPath,
    string? OutputPath,
    IReadOnlyList<string>? RuleSet,
    string? LogLevel);

/// <summary>
/// Dome CLI 解析结果记录。
/// </summary>
/// <param name="IsSuccess">是否解析成功。</param>
/// <param name="Request">运行请求。</param>
/// <param name="ErrorMessage">错误信息。</param>
public sealed record DomeCliParseResult(
    bool IsSuccess,
    RunRequest? Request,
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
          dome --config <config-path>
        """;

    /// <summary>
    /// 异步解析命令行参数。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果。</returns>
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

    /// <summary>
    /// 异步解析配置文件。
    /// </summary>
    /// <param name="configPath">配置文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果。</returns>
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
}
