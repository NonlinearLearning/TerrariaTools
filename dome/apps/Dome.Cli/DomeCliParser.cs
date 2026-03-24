using System.Text.Json;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Dome.Cli;

/// <summary>
/// 描述配置文件中的 CLI 运行参数。
/// </summary>
/// <param name="Command">命令名称。</param>
/// <param name="InputPath">输入路径。</param>
/// <param name="OutputPath">输出路径。</param>
/// <param name="SeedMemberName">影子提取使用的种子成员名。</param>
/// <param name="RuleSet">可选规则集。</param>
/// <param name="LogLevel">可选日志级别。</param>
/// <param name="Loader">可选的加载器偏好。</param>
/// <param name="AllowFallbackToSourceOnly">是否允许回退到源码模式。</param>
public sealed record DomeCliRunConfiguration(
    string? Command,
    string? InputPath,
    string? OutputPath,
    string? SeedMemberName,
    IReadOnlyList<string>? RuleSet,
    string? LogLevel,
    string? Loader,
    bool? AllowFallbackToSourceOnly);

/// <summary>
/// 表示 CLI 支持的命令类型。
/// </summary>
public enum DomeCliCommandKind
{
    /// <summary>
    /// 标准 Dome 工作流。
    /// </summary>
    Standard,

    /// <summary>
    /// 运行时工作流。
    /// </summary>
    Runtime,

    /// <summary>
    /// 影子提取工作流。
    /// </summary>
    ShadowExtraction
}

/// <summary>
/// 表示一次已解析的 CLI 调用。
/// </summary>
/// <param name="Kind">命令类型。</param>
/// <param name="StandardRequest">标准工作流请求。</param>
/// <param name="RuntimeRequest">运行时工作流请求。</param>
/// <param name="ShadowRequest">影子提取工作流请求。</param>
public sealed record DomeCliInvocation(
    DomeCliCommandKind Kind,
    ApplicationAbstractions.RunRequest? StandardRequest,
    ApplicationAbstractions.TerrariaRuntimeRunRequest? RuntimeRequest,
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest? ShadowRequest)
{
    /// <summary>
    /// 创建标准工作流调用。
    /// </summary>
    /// <param name="request">标准工作流请求。</param>
    /// <returns>标准工作流调用。</returns>
    public static DomeCliInvocation Standard(ApplicationAbstractions.RunRequest request) =>
        new(DomeCliCommandKind.Standard, request, null, null);

    /// <summary>
    /// 创建运行时工作流调用。
    /// </summary>
    /// <param name="request">运行时工作流请求。</param>
    /// <returns>运行时工作流调用。</returns>
    public static DomeCliInvocation Runtime(ApplicationAbstractions.TerrariaRuntimeRunRequest request) =>
        new(DomeCliCommandKind.Runtime, null, request, null);

    /// <summary>
    /// 创建影子提取工作流调用。
    /// </summary>
    /// <param name="request">影子提取工作流请求。</param>
    /// <returns>影子提取工作流调用。</returns>
    public static DomeCliInvocation ShadowExtraction(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request) =>
        new(DomeCliCommandKind.ShadowExtraction, null, null, request);
}

/// <summary>
/// 表示一次 CLI 解析结果。
/// </summary>
/// <param name="IsSuccess">指示解析是否成功。</param>
/// <param name="Invocation">成功时的 CLI 调用。</param>
/// <param name="ErrorMessage">失败时的错误消息。</param>
public sealed record DomeCliCommandParseResult(
    bool IsSuccess,
    DomeCliInvocation? Invocation,
    string? ErrorMessage)
{
    /// <summary>
    /// 获取标准工作流请求。
    /// </summary>
    public ApplicationAbstractions.RunRequest? Request => Invocation?.StandardRequest;
}

/// <summary>
/// 负责将命令行参数解析为应用请求。
/// </summary>
public static class DomeCliParser
{
    /// <summary>
    /// 获取 CLI 用法说明文本。
    /// </summary>
    public const string UsageText = """
        Usage:
          dome run <input-path> <output-path> [--loader auto|codeanalysis|sourceonly] [--no-fallback]
          dome analyze <input-path> <output-path> [--loader auto|codeanalysis|sourceonly] [--no-fallback]
          dome plan <input-path> <output-path> [--loader auto|codeanalysis|sourceonly] [--no-fallback]
          dome tr-run <solution-path> <output-path>
          dome tr-shadow <solution-path> <output-path> <seed-member-name>
          dome --config <config-path>
        """;

    /// <summary>
    /// 解析命令行参数。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>CLI 解析结果。</returns>
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

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "run" or "analyze" or "plan" => ParseStandardArguments(command, args),
            "tr-run" => ParseRuntimeArguments(args),
            "tr-shadow" => ParseShadowArguments(args),
            _ => new DomeCliCommandParseResult(false, null, $"Unknown command '{args[0]}'.{Environment.NewLine}{UsageText}")
        };
    }

    /// <summary>
    /// 解析配置文件中的 JSON 文本。
    /// </summary>
    /// <param name="json">配置文件 JSON 文本。</param>
    /// <returns>CLI 解析结果。</returns>
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

    /// <summary>
    /// 从配置文件路径读取并解析配置。
    /// </summary>
    /// <param name="configPath">配置文件路径。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>CLI 解析结果。</returns>
    private static async Task<DomeCliCommandParseResult> ParseConfigFileAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new DomeCliCommandParseResult(false, null, $"Config file '{configPath}' was not found.");
        }

        var json = await File.ReadAllTextAsync(configPath, cancellationToken);
        return ParseConfigJson(json);
    }

    /// <summary>
    /// 根据配置模型构建 CLI 调用。
    /// </summary>
    /// <param name="config">配置模型。</param>
    /// <returns>CLI 解析结果。</returns>
    private static DomeCliCommandParseResult ParseConfig(DomeCliRunConfiguration? config)
    {
        if (config?.InputPath == null || config.OutputPath == null)
        {
            return new DomeCliCommandParseResult(false, null, "Config file must specify InputPath and OutputPath.");
        }

        var command = (config.Command ?? "run").ToLowerInvariant();
        return command switch
        {
            "run" or "analyze" or "plan" => ParseStandardConfig(command, config),
            "tr-run" => new DomeCliCommandParseResult(
                true,
                DomeCliInvocation.Runtime(new ApplicationAbstractions.TerrariaRuntimeRunRequest(config.InputPath, config.OutputPath)),
                null),
            "tr-shadow" => string.IsNullOrWhiteSpace(config.SeedMemberName)
                ? new DomeCliCommandParseResult(false, null, "Shadow extraction config must specify SeedMemberName.")
                : new DomeCliCommandParseResult(
                    true,
                    DomeCliInvocation.ShadowExtraction(
                        new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(config.InputPath, config.OutputPath, config.SeedMemberName)),
                    null),
            _ => new DomeCliCommandParseResult(false, null, $"Unknown config command '{command}'.")
        };
    }

    /// <summary>
    /// 解析标准工作流命令行参数。
    /// </summary>
    /// <param name="command">命令名称。</param>
    /// <param name="args">命令行参数数组。</param>
    /// <returns>CLI 解析结果。</returns>
    private static DomeCliCommandParseResult ParseStandardArguments(string command, string[] args)
    {
        if (args.Length < 3)
        {
            return new DomeCliCommandParseResult(false, null, UsageText);
        }

        if (!TryParseMode(command, out var mode))
        {
            return new DomeCliCommandParseResult(false, null, $"Unknown command '{command}'.{Environment.NewLine}{UsageText}");
        }

        var optionsResult = ParseWorkspaceLoadOptions(args.Skip(3).ToArray());
        if (!optionsResult.IsSuccess)
        {
            return new DomeCliCommandParseResult(false, null, optionsResult.ErrorMessage);
        }

        return new DomeCliCommandParseResult(
            true,
            DomeCliInvocation.Standard(
                new ApplicationAbstractions.RunRequest(args[1], args[2], Array.Empty<string>(), mode, optionsResult.Options!)),
            null);
    }

    /// <summary>
    /// 解析运行时工作流命令行参数。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <returns>CLI 解析结果。</returns>
    private static DomeCliCommandParseResult ParseRuntimeArguments(string[] args)
    {
        if (args.Length != 3)
        {
            return new DomeCliCommandParseResult(false, null, UsageText);
        }

        return new DomeCliCommandParseResult(
            true,
            DomeCliInvocation.Runtime(new ApplicationAbstractions.TerrariaRuntimeRunRequest(args[1], args[2])),
            null);
    }

    /// <summary>
    /// 解析影子提取工作流命令行参数。
    /// </summary>
    /// <param name="args">命令行参数数组。</param>
    /// <returns>CLI 解析结果。</returns>
    private static DomeCliCommandParseResult ParseShadowArguments(string[] args)
    {
        if (args.Length != 4)
        {
            return new DomeCliCommandParseResult(false, null, UsageText);
        }

        return new DomeCliCommandParseResult(
            true,
            DomeCliInvocation.ShadowExtraction(new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(args[1], args[2], args[3])),
            null);
    }

    /// <summary>
    /// 解析标准工作流配置对象。
    /// </summary>
    /// <param name="command">命令名称。</param>
    /// <param name="config">配置对象。</param>
    /// <returns>CLI 解析结果。</returns>
    private static DomeCliCommandParseResult ParseStandardConfig(string command, DomeCliRunConfiguration config)
    {
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
            DomeCliInvocation.Standard(
                new ApplicationAbstractions.RunRequest(
                    config.InputPath!,
                    config.OutputPath!,
                    config.RuleSet ?? Array.Empty<string>(),
                    mode,
                    BuildWorkspaceLoadOptions(config.Loader, config.AllowFallbackToSourceOnly))),
            null);
    }

    /// <summary>
    /// 从可选参数中构建工作区加载选项。
    /// </summary>
    /// <param name="args">可选参数数组。</param>
    /// <returns>是否成功、构建出的选项和错误消息。</returns>
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

    /// <summary>
    /// 根据配置字段构建工作区加载选项。
    /// </summary>
    /// <param name="loader">加载器偏好文本。</param>
    /// <param name="allowFallback">是否允许回退。</param>
    /// <returns>工作区加载选项。</returns>
    private static ApplicationAbstractions.WorkspaceLoadOptions BuildWorkspaceLoadOptions(string? loader, bool? allowFallback)
    {
        var preferredLoader = ApplicationAbstractions.WorkspaceLoaderPreference.Auto;
        if (!string.IsNullOrWhiteSpace(loader) && TryParseLoaderPreference(loader, out var parsedPreference))
        {
            preferredLoader = parsedPreference;
        }

        return new ApplicationAbstractions.WorkspaceLoadOptions(preferredLoader, allowFallback ?? true);
    }

    /// <summary>
    /// 将命令名解析为运行模式。
    /// </summary>
    /// <param name="command">命令名称。</param>
    /// <param name="mode">解析得到的运行模式。</param>
    /// <returns>如果命令受支持则返回 <see langword="true"/>。</returns>
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

    /// <summary>
    /// 将加载器偏好文本解析为枚举值。
    /// </summary>
    /// <param name="value">加载器偏好文本。</param>
    /// <param name="preference">解析得到的加载器偏好。</param>
    /// <returns>如果值受支持则返回 <see langword="true"/>。</returns>
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
