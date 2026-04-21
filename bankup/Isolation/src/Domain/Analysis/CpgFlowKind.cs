namespace Domain.Analysis;

/// <summary>
/// 表示 CPG 控制流类型。
/// </summary>
public enum CpgFlowKind
{
    /// <summary>
    /// 顺序流。
    /// </summary>
    Sequential = 1,

    /// <summary>
    /// 条件流。
    /// </summary>
    Conditional = 2,

    /// <summary>
    /// 返回流。
    /// </summary>
    Return = 3,
}
