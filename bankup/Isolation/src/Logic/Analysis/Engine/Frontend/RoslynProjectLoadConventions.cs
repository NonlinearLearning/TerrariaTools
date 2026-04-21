namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛 Roslyn 项目加载阶段的输入识别与错误消息规则。
/// </summary>
public static class RoslynProjectLoadConventions
{
    /// <summary>
    /// 判断是否为解决方案文件。
    /// </summary>
    public static bool IsSolutionFile(string? path) => HasExtension(path, ".sln");

    /// <summary>
    /// 判断是否为项目文件。
    /// </summary>
    public static bool IsProjectFile(string? path) => HasExtension(path, ".csproj");

    /// <summary>
    /// 判断是否为 C# 源文件。
    /// </summary>
    public static bool IsSourceFile(string? path) => HasExtension(path, ".cs");

    /// <summary>
    /// 构造不支持的输入文件类型错误。
    /// </summary>
    public static string BuildUnsupportedFileTypeMessage(string? fullPath)
    {
        return $"不支持的输入文件类型：'{fullPath}'。";
    }

    /// <summary>
    /// 构造输入路径不存在错误。
    /// </summary>
    public static string BuildMissingInputPathMessage(string? fullPath)
    {
        return $"输入路径不存在：'{fullPath}'。";
    }

    /// <summary>
    /// 构造目录无源码错误。
    /// </summary>
    public static string BuildNoSourceFilesMessage(string? directoryPath)
    {
        return $"目录 '{directoryPath}' 中没有找到任何 C# 源文件。";
    }

    /// <summary>
    /// 构造无可分析项目错误。
    /// </summary>
    public static string BuildNoAnalyzableProjectsMessage()
    {
        return "解决方案里没有可分析的 C# 项目。";
    }

    /// <summary>
    /// 构造内存项目创建失败错误。
    /// </summary>
    public static string BuildInMemoryProjectCreationFailedMessage()
    {
        return "无法创建内存项目。";
    }

    /// <summary>
    /// 构造 Compilation 创建失败错误。
    /// </summary>
    public static string BuildCompilationCreationFailedMessage()
    {
        return "无法为当前项目创建 Compilation。";
    }

    /// <summary>
    /// 构造基础程序集列表缺失错误。
    /// </summary>
    public static string BuildMissingTrustedPlatformAssembliesMessage()
    {
        return "当前运行环境没有可用的基础程序集列表。";
    }

    private static bool HasExtension(string? path, string extension)
    {
        return Path.GetExtension(path ?? string.Empty).Equals(extension, StringComparison.OrdinalIgnoreCase);
    }
}
