using System.Text.RegularExpressions;

namespace Analysis.X2Cpg;

/// <summary>
/// 保存 x2cpg 前端共享配置。
///
/// 对应 Joern `X2CpgConfig.GenericConfig` 和 `X2CpgConfig` trait。C# 版不复制
/// 命令行解析器，只保留核心建图需要的字段和 `With...` 派生方法。
/// </summary>
public sealed record X2CpgConfig
{
    /// <summary>
    /// Joern 默认输出路径。空字符串表示内存态 CPG。
    /// </summary>
    public const string DefaultOutputPath = "";

    /// <summary>
    /// 获取输入目录的绝对路径。
    /// </summary>
    public string InputPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取输出文件路径。空字符串表示不写图存储。
    /// </summary>
    public string OutputPath { get; init; } = DefaultOutputPath;

    /// <summary>
    /// 获取是否以服务模式运行。
    /// </summary>
    public bool ServerMode { get; init; }

    /// <summary>
    /// 获取服务模式超时时间。
    /// </summary>
    public TimeSpan? ServerTimeout { get; init; }

    /// <summary>
    /// 获取默认忽略文件正则。
    /// </summary>
    public IReadOnlyList<Regex> DefaultIgnoredFilesRegex { get; init; } = Array.Empty<Regex>();

    /// <summary>
    /// 获取用户提供的忽略文件正则。
    /// </summary>
    public Regex IgnoredFilesRegex { get; init; } = new(string.Empty, RegexOptions.Compiled);

    /// <summary>
    /// 获取需要忽略的绝对文件或目录路径。
    /// </summary>
    public IReadOnlyList<string> IgnoredFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取 schema 校验模式。
    /// </summary>
    public ValidationMode SchemaValidation { get; init; } = ValidationMode.Disabled;

    /// <summary>
    /// 获取是否跳过 FILE 节点的源码内容。
    /// </summary>
    public bool DisableFileContent { get; init; } = true;

    /// <summary>
    /// 使用输入路径创建新配置。
    /// </summary>
    /// <param name="inputPath">输入文件或目录路径。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithInputPath(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        return this with { InputPath = Path.GetFullPath(inputPath) };
    }

    /// <summary>
    /// 使用输出路径创建新配置。
    /// </summary>
    /// <param name="outputPath">输出文件路径。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithOutputPath(string outputPath)
    {
        return this with { OutputPath = outputPath ?? string.Empty };
    }

    /// <summary>
    /// 使用服务模式开关创建新配置。
    /// </summary>
    /// <param name="serverMode">是否启用服务模式。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithServerMode(bool serverMode)
    {
        return this with { ServerMode = serverMode };
    }

    /// <summary>
    /// 使用服务超时时间创建新配置。
    /// </summary>
    /// <param name="seconds">超时秒数。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithServerTimeoutSeconds(long seconds)
    {
        if (seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "超时时间不能小于 0。");
        }

        return this with { ServerTimeout = TimeSpan.FromSeconds(seconds) };
    }

    /// <summary>
    /// 使用默认忽略正则创建新配置。
    /// </summary>
    /// <param name="regexes">默认忽略正则。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithDefaultIgnoredFilesRegex(IEnumerable<Regex> regexes)
    {
        ArgumentNullException.ThrowIfNull(regexes);
        return this with { DefaultIgnoredFilesRegex = regexes.ToArray() };
    }

    /// <summary>
    /// 使用用户忽略正则创建新配置。
    /// </summary>
    /// <param name="pattern">正则表达式文本。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithIgnoredFilesRegex(string pattern)
    {
        return this with { IgnoredFilesRegex = new Regex(pattern ?? string.Empty, RegexOptions.Compiled) };
    }

    /// <summary>
    /// 使用忽略文件集合创建新配置。
    ///
    /// 相对路径会按 Joern 行为绑定到当前 `InputPath` 下，再转成绝对路径。
    /// </summary>
    /// <param name="paths">要忽略的文件或目录路径。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithIgnoredFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string basePath = string.IsNullOrWhiteSpace(InputPath) ? Directory.GetCurrentDirectory() : InputPath;
        string[] ignoredFiles = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.IsPathFullyQualified(path) ? path : Path.Combine(basePath, path))
            .Select(Path.GetFullPath)
            .ToArray();

        return this with { IgnoredFiles = ignoredFiles };
    }

    /// <summary>
    /// 使用 schema 校验模式创建新配置。
    /// </summary>
    /// <param name="validationMode">校验模式。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithSchemaValidation(ValidationMode validationMode)
    {
        return this with { SchemaValidation = validationMode };
    }

    /// <summary>
    /// 使用 FILE 节点源码内容开关创建新配置。
    /// </summary>
    /// <param name="disableFileContent">是否禁用源码内容。</param>
    /// <returns>更新后的配置。</returns>
    public X2CpgConfig WithDisableFileContent(bool disableFileContent)
    {
        return this with { DisableFileContent = disableFileContent };
    }
}
