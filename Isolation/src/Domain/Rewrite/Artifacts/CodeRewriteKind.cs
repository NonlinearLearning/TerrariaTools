namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示代码重写动作类型。
/// </summary>
public enum CodeRewriteKind
{
    /// <summary>
    /// 删除类型。
    /// </summary>
    DeleteClass = 1,

    /// <summary>
    /// 删除方法。
    /// </summary>
    DeleteMethod = 2,

    /// <summary>
    /// 方法私有化。
    /// </summary>
    PrivatizeMethod = 3,

    /// <summary>
    /// 清空方法体。
    /// </summary>
    ClearMethodBody = 4,
}
