namespace NLISSN.Application;

/// <summary>
/// 文件按**方法体总行数**划分的大小档。
/// <para>
/// 分档只用于决定「这个文件是否需要切成 K 片」：
/// <see cref="Small"/> 与 <see cref="Medium"/> 不需要拆分（K=1），
/// <see cref="Large"/> 需要按目标片行数切成 K 片。
/// </para>
/// <para>
/// 口径与阈值来自 <see cref="DocumentShardPlannerOptions"/>，**不得**在任何 pass 中写死。
/// </para>
/// </summary>
public enum FileSizeClass
{
    /// <summary>方法体总行数不超过 <see cref="DocumentShardPlannerOptions.SmallFileMaxLines"/>。</summary>
    Small,

    /// <summary>
    /// 方法体总行数超过小文件阈值，但不超过 <see cref="DocumentShardPlannerOptions.MediumFileMaxLines"/>。
    /// </summary>
    Medium,

    /// <summary>方法体总行数超过中文件阈值，需要切成 K 个分片。</summary>
    Large,
}
