namespace Analysis.Semantic.Flows;

/// <summary>
/// 从外部规则文件加载方法语义规则。
///
/// 当前采用最小文本格式：
/// `MethodFullName SOURCE TARGET`
///
/// 例如：
/// `Demo.Flow.Copy(string,string) ARG[1] RET`
/// </summary>
public static class MethodFlowRuleLoader
{
    /// <summary>
    /// 从文件加载规则集。
    /// </summary>
    /// <param name="filePath">规则文件路径。</param>
    /// <returns>加载得到的规则集。</returns>
    public static MethodFlowRuleSet LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        MethodFlowRuleSet ruleSet = new();
        foreach (string rawLine in File.ReadAllLines(filePath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
            {
                throw new InvalidOperationException($"无法解析语义规则：'{line}'。");
            }

            bool isRegex = parts[0].StartsWith("regex:", StringComparison.Ordinal);
            string methodFullName = isRegex ? parts[0]["regex:".Length..] : parts[0];
            ruleSet.Add(new MethodFlowRule(
                methodFullName,
                MethodFlowRuleParser.ParseEndpoint(parts[1]),
                MethodFlowRuleParser.ParseEndpoint(parts[2]),
                isRegex));
        }

        return ruleSet;
    }
}
