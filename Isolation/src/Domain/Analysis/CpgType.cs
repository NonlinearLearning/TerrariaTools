namespace Domain.Analysis;

/// <summary>
/// 表示 CPG 中的类型分类。
/// </summary>
public enum CpgType
{
    /// <summary>
    /// 未知类型。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 文件节点。
    /// </summary>
    File = 1,

    /// <summary>
    /// 类型节点。
    /// </summary>
    TypeDecl = 2,

    /// <summary>
    /// 方法节点。
    /// </summary>
    Method = 3,

    /// <summary>
    /// 调用节点。
    /// </summary>
    Call = 4,
}
