namespace Analysis.Frontend;

/// <summary>
/// 表示类型桩配置。
/// </summary>
public sealed record TypeStubsParserConfig(string? TypeStubsFilePath = null);

/// <summary>
/// 表示一个类型桩条目。
///
/// 当前只保留后续最容易消费的稳定字段，不把格式耦合到某一门语言前端。
/// </summary>
public sealed record TypeStubEntry(
    string FullName,
    string Kind,
    IReadOnlyCollection<string> Members);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// 基于输入路径创建配置对象。
    /// </summary>
    public static TypeStubsParserConfig CreateConfig(string? typeStubsFilePath)
    {
        return string.IsNullOrWhiteSpace(typeStubsFilePath)
            ? new TypeStubsParserConfig()
            : new TypeStubsParserConfig(NormalizePath(typeStubsFilePath));
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
        return ParseLines(File.ReadAllLines(path));
    }

    /// <summary>
    /// 从文本行解析类型桩条目。
    /// </summary>
    public static IReadOnlyCollection<TypeStubEntry> ParseLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<TypeStubEntry> entries = new();
        foreach (string rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            string line = rawLine.Trim();
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('|');
            string kind;
            string fullName;
            string[] members;

            if (parts.Length >= 3)
            {
                kind = NormalizeKind(parts[0]);
                fullName = parts[1].Trim();
                members = SplitMembers(parts[2]);
            }
            else if (parts.Length == 2)
            {
                kind = NormalizeKind(parts[0]);
                fullName = parts[1].Trim();
                members = Array.Empty<string>();
            }
            else
            {
                kind = "TYPE";
                fullName = parts[0].Trim();
                members = Array.Empty<string>();
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            entries.Add(new TypeStubEntry(fullName, kind, members));
        }

        return entries;
    }

    private static string NormalizeKind(string rawKind)
    {
        return string.IsNullOrWhiteSpace(rawKind)
            ? "TYPE"
            : rawKind.Trim().ToUpperInvariant();
    }

    private static string[] SplitMembers(string rawMembers)
    {
        return rawMembers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
