namespace Logic.Analysis.Engine.X2Cpg;

/// <summary>
/// 收敛源文件发现阶段的纯规则。
/// </summary>
public static class SourceFileDiscoveryRules
{
    /// <summary>
    /// 默认最大文件大小。
    /// </summary>
    public const long DefaultMaxFileSizeBytes = int.MaxValue - 2L;

    /// <summary>
    /// 判断文件大小是否允许被分析。
    /// </summary>
    public static bool IsFileSizeAllowed(long fileSizeBytes, long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        return fileSizeBytes <= maxFileSizeBytes;
    }

    /// <summary>
    /// 判断文件路径是否匹配源码扩展名集合。
    /// </summary>
    public static bool HasSupportedExtension(string filePath, IEnumerable<string> sourceFileExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(sourceFileExtensions);

        return sourceFileExtensions.Any(extension =>
            filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按稳定顺序排序源文件。
    /// </summary>
    public static IReadOnlyList<string> OrderFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.OrderBy(file => file, StringComparer.Ordinal).ToArray();
    }
}
