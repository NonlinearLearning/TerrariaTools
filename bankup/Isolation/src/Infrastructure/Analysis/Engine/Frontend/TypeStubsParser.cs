using Logic.Analysis.Engine.Frontend;

namespace Infrastructure.Analysis.Engine.Frontend;

/// <summary>
/// 提供类型桩文件路径标准化与最小解析能力。
///
/// Joern 原版 `XTypeStubsParser.scala` 更偏命令行参数扩展点。
/// 当前 C# 版把它落成一个更直接可用的工具：既能标准化路径，
/// 也能把简单文本格式解析成结构化条目。
/// </summary>
public static class TypeStubsParser
{
    /// <summary>
    /// 将输入路径标准化为绝对路径。
    /// </summary>
    public static string NormalizePath(string path)
    {
        return TypeStubsPathConventions.NormalizePath(path);
    }

    /// <summary>
    /// 基于输入路径创建配置对象。
    /// </summary>
    public static TypeStubsParserConfig CreateConfig(string? typeStubsFilePath)
    {
        return TypeStubsPathConventions.CreateConfig(typeStubsFilePath);
    }

    /// <summary>
    /// 从文件解析类型桩条目。
    ///
    /// 当前支持的最小格式如下：
    /// - 空行与 `#` 注释行忽略；
    /// - 一行一个类型；
    /// - 语法为 `Kind|FullName|Member1,Member2,...`；
    /// - `Kind` 与成员列表都可省略。
    /// </summary>
    public static IReadOnlyCollection<TypeStubEntry> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return TypeStubsParserCore.ParseLines(File.ReadAllLines(path));
    }

    /// <summary>
    /// 从文本行解析类型桩条目。
    /// </summary>
    public static IReadOnlyCollection<TypeStubEntry> ParseLines(IEnumerable<string> lines)
    {
        return TypeStubsParserCore.ParseLines(lines);
    }
}
