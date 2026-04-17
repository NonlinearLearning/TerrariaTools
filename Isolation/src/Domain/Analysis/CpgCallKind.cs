namespace Domain.Analysis;

/// <summary>
/// 表示 CPG 调用类型。
/// </summary>
public enum CpgCallKind
{
    /// <summary>
    /// 静态调用。
    /// </summary>
    Static = 1,

    /// <summary>
    /// 实例调用。
    /// </summary>
    Instance = 2,

    /// <summary>
    /// 动态调用。
    /// </summary>
    Dynamic = 3,
}
