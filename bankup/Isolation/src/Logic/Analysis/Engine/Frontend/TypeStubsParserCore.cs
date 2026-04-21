namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 提供类型桩文本的纯解析能力。
/// </summary>
public static class TypeStubsParserCore
{
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
