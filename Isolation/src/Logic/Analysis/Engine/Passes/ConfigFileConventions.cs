namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 收敛配置文件识别与扫描根路径规则。
/// </summary>
public static class ConfigFileConventions
{
    private static readonly string[] SupportedExtensions =
    [
        ".json",
        ".yaml",
        ".yml",
        ".xml",
        ".config",
        ".props",
        ".targets",
    ];

    private static readonly string[] SupportedFileNames =
    [
        "appsettings.json",
        "nuget.config",
        "Directory.Build.props",
        "Directory.Build.targets",
    ];

    /// <summary>
    /// 解析扫描根路径。
    /// </summary>
    public static string ResolveRootPath(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return string.Empty;
        }

        return Directory.Exists(inputPath)
            ? inputPath
            : Path.GetDirectoryName(inputPath) ?? string.Empty;
    }

    /// <summary>
    /// 判断是否为配置文件。
    /// </summary>
    public static bool IsConfigFile(string? filePath)
    {
        string fileName = Path.GetFileName(filePath);
        if (SupportedFileNames.Any(supported =>
                string.Equals(fileName, supported, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string extension = Path.GetExtension(filePath);
        return SupportedExtensions.Any(supported =>
            string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase));
    }
}
