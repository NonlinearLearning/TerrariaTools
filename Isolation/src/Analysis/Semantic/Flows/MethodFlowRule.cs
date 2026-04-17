namespace Analysis.Semantic.Flows;

/// <summary>
/// 表示一条“方法内部如何传播数据”的规则。
///
/// 例如：
/// - `ARG[1] -> RET`
/// - `ARG[2] -> RET`
/// - `RECEIVER -> RET`
/// </summary>
public sealed class MethodFlowRule
{
    /// <summary>
    /// 使用完整规则信息初始化对象。
    /// </summary>
    /// <param name="methodFullName">方法完整名。</param>
    /// <param name="source">流入端。</param>
    /// <param name="target">流出端。</param>
    public MethodFlowRule(string methodFullName, FlowEndpoint source, FlowEndpoint target, bool isRegex = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodFullName);
        MethodFullName = methodFullName;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        IsRegex = isRegex;
    }

    /// <summary>
    /// 获取规则所属的方法完整名。
    /// </summary>
    public string MethodFullName { get; }

    /// <summary>
    /// 获取规则的源端点。
    /// </summary>
    public FlowEndpoint Source { get; }

    /// <summary>
    /// 获取规则的目标端点。
    /// </summary>
    public FlowEndpoint Target { get; }

    /// <summary>
    /// 获取该规则是否使用正则匹配方法完整名。
    /// </summary>
    public bool IsRegex { get; }
}
