namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛类型桩路径和配置构造的纯规则。
/// </summary>
public static class TypeStubsPathConventions
{
    /// <summary>
    /// 将路径标准化为绝对路径。
    /// </summary>
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// 基于输入路径构造类型桩配置。
    /// </summary>
    public static TypeStubsParserConfig CreateConfig(string? typeStubsFilePath)
    {
        return string.IsNullOrWhiteSpace(typeStubsFilePath)
            ? new TypeStubsParserConfig()
            : new TypeStubsParserConfig(NormalizePath(typeStubsFilePath));
    }
}
