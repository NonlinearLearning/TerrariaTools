namespace Analysis.Semantic.Flows;

/// <summary>
/// 表示一条方法语义规则中的一端。
///
/// 例如：
/// - `ARG[1]` 表示第一个实参；
/// - `RET` 表示返回值；
/// - `RECEIVER` 表示实例调用的接收者。
/// </summary>
public sealed class FlowEndpoint
{
    /// <summary>
    /// 使用端点类型和可选参数序号初始化对象。
    /// </summary>
    /// <param name="kind">端点类别。</param>
    /// <param name="argumentIndex">参数序号，仅在参数端点时有值。</param>
    public FlowEndpoint(FlowEndpointKind kind, int? argumentIndex = null)
    {
        if (kind == FlowEndpointKind.Argument)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(argumentIndex ?? 0);
        }

        Kind = kind;
        ArgumentIndex = argumentIndex;
    }

    /// <summary>
    /// 获取端点类别。
    /// </summary>
    public FlowEndpointKind Kind { get; }

    /// <summary>
    /// 获取参数序号。
    /// </summary>
    public int? ArgumentIndex { get; }
}

/// <summary>
/// 定义方法语义规则中支持的端点类型。
/// </summary>
public enum FlowEndpointKind
{
    Receiver = 0,
    Argument = 1,
    Return = 2,
}
