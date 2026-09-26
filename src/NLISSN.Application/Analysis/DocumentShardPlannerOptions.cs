namespace NLISSN.Application;

/// <summary>
/// <see cref="DocumentShardPlanner"/> 的阈值配置。
/// <para>
/// 设计文档明确要求：阈值必须是本配置的字段，**不得写死在 pass 中**
/// （docs\plans\2026-09-24-unified-work-scheduler-design.md:439）。
/// </para>
/// <para>
/// ⚠ 默认值只是**建议初值，尚待实测校准**（同上文档 :440-446 与执行计划 S5-4），
/// 不是经过验证的最优值；调用方可按语料实测结果整体替换。
/// </para>
/// </summary>
public sealed record DocumentShardPlannerOptions
{
    /// <summary>
    /// 建议初值：小文件上限 200 行、中文件上限 800 行、大文件目标片 1500 行。
    /// <para>前两者与 <c>CpgWorkBatchCostOptions.Default</c> 的 <c>MediumMaxCost</c>/<c>LargeMaxCost</c>
    /// 同量级（src\NLCPG\Builder\Concurrency\CpgWorkBatchCostModel.cs:58-63），便于两层口径对齐。</para>
    /// </summary>
    public static DocumentShardPlannerOptions Default { get; } = new(
      smallFileMaxLines: 200,
      mediumFileMaxLines: 800,
      largeFileShardTargetLines: 1500);

    /// <summary>构造阈值配置；三项必须满足 <c>0 &lt; SmallFileMaxLines &lt; MediumFileMaxLines</c>
    /// 且目标片行数不小于 1。</summary>
    /// <param name="smallFileMaxLines">小文件的方法体总行数上限（含）。</param>
    /// <param name="mediumFileMaxLines">中文件的方法体总行数上限（含），必须严格大于小文件上限。</param>
    /// <param name="largeFileShardTargetLines">大文件每个分片的目标方法体行数。</param>
    public DocumentShardPlannerOptions(
      int smallFileMaxLines,
      int mediumFileMaxLines,
      int largeFileShardTargetLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(smallFileMaxLines, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(largeFileShardTargetLines, 1);
        if (mediumFileMaxLines <= smallFileMaxLines)
        {
            throw new ArgumentOutOfRangeException(nameof(mediumFileMaxLines));
        }

        SmallFileMaxLines = smallFileMaxLines;
        MediumFileMaxLines = mediumFileMaxLines;
        LargeFileShardTargetLines = largeFileShardTargetLines;
    }

    /// <summary>小文件的方法体总行数上限（含）。建议初值 200。</summary>
    public int SmallFileMaxLines { get; }

    /// <summary>中文件的方法体总行数上限（含）。建议初值 800。</summary>
    public int MediumFileMaxLines { get; }

    /// <summary>大文件每个分片的目标方法体行数。建议初值 1500。</summary>
    public int LargeFileShardTargetLines { get; }
}
