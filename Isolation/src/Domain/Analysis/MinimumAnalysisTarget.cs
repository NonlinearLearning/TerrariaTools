namespace Domain.Analysis;

/// <summary>
/// 表示最小分析目标类型。
/// </summary>
public enum MinimumAnalysisTarget
{
    /// <summary>
    /// 文件级别。
    /// </summary>
    File = 1,

    /// <summary>
    /// 类型级别。
    /// </summary>
    Type = 2,

    /// <summary>
    /// 方法级别。
    /// </summary>
    Method = 3,

    /// <summary>
    /// 语句级别。
    /// </summary>
    Statement = 4,
}
