namespace Analysis.Semantic.Flows;

/// <summary>
/// 解析完整名语义规则文本。
///
/// 支持的最小格式：
/// - `MethodFullName SOURCE TARGET`
/// - `regex:Pattern SOURCE TARGET`
/// </summary>
public sealed class FullNameSemanticsParser
{
    /// <summary>
    /// 解析规则文本。
    /// </summary>
    /// <param name="text">规则文本。</param>
    /// <returns>解析得到的规则。</returns>
    public IReadOnlyList<MethodFlowRule> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<MethodFlowRule> rules = new();
        foreach (string rawLine in text.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            rules.Add(ParseLine(line));
        }

        return rules;
    }

    /// <summary>
    /// 解析单行规则。
    /// </summary>
    /// <param name="line">规则行。</param>
    /// <returns>解析得到的规则。</returns>
    public MethodFlowRule ParseLine(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException($"无法解析语义规则：'{line}'。");
        }

        bool isRegex = parts[0].StartsWith("regex:", StringComparison.Ordinal);
        string methodFullName = isRegex ? parts[0]["regex:".Length..] : parts[0];
        return new MethodFlowRule(
            methodFullName,
            MethodFlowRuleParser.ParseEndpoint(parts[1]),
            MethodFlowRuleParser.ParseEndpoint(parts[2]),
            isRegex);
    }
}
