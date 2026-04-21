namespace Domain.Analysis.Engine.Semantic.Flows;

/// <summary>
/// 提供方法流规则的公共解析函数。
/// </summary>
public static class MethodFlowRuleParser
{
    /// <summary>
    /// 解析端点标记。
    /// </summary>
    /// <param name="token">端点标记。</param>
    /// <returns>端点对象。</returns>
    public static FlowEndpoint ParseEndpoint(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (string.Equals(token, "RET", StringComparison.Ordinal))
        {
            return new FlowEndpoint(FlowEndpointKind.Return);
        }

        if (string.Equals(token, "RECEIVER", StringComparison.Ordinal))
        {
            return new FlowEndpoint(FlowEndpointKind.Receiver);
        }

        if (token.StartsWith("ARG[", StringComparison.Ordinal) &&
            token.EndsWith(']') &&
            int.TryParse(token[4..^1], out int argumentIndex))
        {
            return new FlowEndpoint(FlowEndpointKind.Argument, argumentIndex);
        }

        throw new InvalidOperationException($"无法解析语义端点：'{token}'。");
    }
}
