namespace NLCPG.Builder.Concurrency;

public enum CpgWorkBatchSizeClass
{
    Small,
    Medium,
    Large,
    Oversized,
}

public enum CpgWorkBatchCostFallbackReason
{
    None,
    UnknownSpanCost,
}

public sealed record CpgWorkBatchCostEstimate(
  int Cost,
  CpgWorkBatchSizeClass SizeClass,
  CpgWorkBatchCostFallbackReason FallbackReason);

public sealed record CpgWorkBatchCostOptions
{
    public CpgWorkBatchCostOptions(
      int smallMaxCost,
      int mediumMaxCost,
      int largeMaxCost,
      int targetBatchCost,
      int maxBatchCost)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(smallMaxCost, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(mediumMaxCost, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(largeMaxCost, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetBatchCost, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBatchCost, 1);
        if (smallMaxCost >= mediumMaxCost)
        {
            throw new ArgumentOutOfRangeException(nameof(mediumMaxCost));
        }

        if (mediumMaxCost >= largeMaxCost)
        {
            throw new ArgumentOutOfRangeException(nameof(largeMaxCost));
        }

        if (targetBatchCost > maxBatchCost)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchCost));
        }

        SmallMaxCost = smallMaxCost;
        MediumMaxCost = mediumMaxCost;
        LargeMaxCost = largeMaxCost;
        TargetBatchCost = targetBatchCost;
        MaxBatchCost = maxBatchCost;
        // 默认把「隔离阈值」绑定到 MediumMaxCost，**逐字保留**既有语义：
        // 历史上 CpgWorkBatchBuilder 用 Classify(...) 的 Large/Oversized 判定来决定
        // 「该方法是否独占一批」，而 Large 的下界就是 MediumMaxCost=200。
        // 现有调用点不传本属性 ⇒ 行为与本次改动前完全一致。
        IsolationCostThreshold = mediumMaxCost;
    }

    public static CpgWorkBatchCostOptions Default { get; } = new(
      smallMaxCost: 40,
      mediumMaxCost: 200,
      largeMaxCost: 800,
      targetBatchCost: 400,
      maxBatchCost: 800);

    /// <summary>
    /// **生产装箱策略（跨文件/大文件凑标准大小）。**
    /// <para>
    /// 与 <see cref="Default"/> 的唯一区别是 <see cref="IsolationCostThreshold"/>：
    /// 抬到 <c>LargeFileShardTargetLines</c> 量级（1500），使
    /// <c>cost ∈ (200, 1500]</c> 的「中等偏大」方法**可被配对装箱**，
    /// 而不是各自独占一批。
    /// </para>
    /// <para>
    /// ⚠️ **为什么区分 Default 与 Packing：** <see cref="Default"/> 是「保守基线」，
    /// 被 12 处测试与 <see cref="CpgWorkBatchCostModel.Estimate"/> 的默认参数引用，
    /// 且 <c>CpgWorkBatchBuilderTests</c> 有两条用例**断言** 201/801 会被隔离。
    /// 因此**不改 <see cref="Default"/> 的值**，而是把放宽后的策略作为独立预设，
    /// 由 <c>NLCPGBuilderOptions.EffectiveWorkBatchCostOptions</c> 采用。
    /// </para>
    /// <para>
    /// 实测收益（<c>NPC.cs</c>，1500 标准）：批次数 <b>43 → 21</b>（2.05×），
    /// 单项批 <b>23 → 7</b>（= 成本 &gt; 1500 的完整方法数，即单项批达理论最小）；
    /// 全语料 967 文件：<b>1081 → 768</b> 批（1.41×）。
    /// </para>
    /// </summary>
    public static CpgWorkBatchCostOptions Packing { get; } = new(
      smallMaxCost: 40,
      mediumMaxCost: 1500,
      largeMaxCost: 1550,
      targetBatchCost: 1500,
      maxBatchCost: 1500)
    {
        IsolationCostThreshold = 1500,
    };

    public int SmallMaxCost { get; }

    public int MediumMaxCost { get; }

    public int LargeMaxCost { get; }

    public int TargetBatchCost { get; }

    public int MaxBatchCost { get; }

    /// <summary>
    /// **装箱隔离阈值**：<c>cost</c> 严格大于该值的方法**独占一批**（不与任何项配对）。
    /// <para>
    /// 与「尺寸分类」（<see cref="CpgWorkBatchSizeClass"/>）**解耦**：
    /// 分类是**度量口径**（小/中/大/超大，供遥测与阈值判断），
    /// 而本值是**装箱策略**（哪些方法必须独占）。
    /// 历史上两者共用了 <see cref="MediumMaxCost"/> 这一个常数，导致
    /// 「调分类上界」与「调装箱策略」无法分别进行。
    /// </para>
    /// <para>
    /// 默认等于 <see cref="MediumMaxCost"/>（构造时赋值）⇒ 既有行为逐字不变。
    /// </para>
    /// </summary>
    public int IsolationCostThreshold { get; init; }
}

public static class CpgWorkBatchCostModel
{
    public static CpgWorkBatchCostEstimate Estimate(
      int startLine,
      int endLine,
      CpgWorkBatchCostOptions? options = null)
    {
        var costOptions = options ?? CpgWorkBatchCostOptions.Default;
        var hasKnownSpan = startLine >= 0 && endLine >= startLine;
        var cost = hasKnownSpan
          ? checked(endLine - startLine + 1)
          : 1;
        var fallbackReason = hasKnownSpan
          ? CpgWorkBatchCostFallbackReason.None
          : CpgWorkBatchCostFallbackReason.UnknownSpanCost;
        return new CpgWorkBatchCostEstimate(
          cost,
          Classify(cost, costOptions),
          fallbackReason);
    }

    public static CpgWorkBatchSizeClass Classify(
      int cost,
      CpgWorkBatchCostOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cost, 1);
        ArgumentNullException.ThrowIfNull(options);

        if (cost <= options.SmallMaxCost)
        {
            return CpgWorkBatchSizeClass.Small;
        }

        if (cost <= options.MediumMaxCost)
        {
            return CpgWorkBatchSizeClass.Medium;
        }

        if (cost <= options.LargeMaxCost)
        {
            return CpgWorkBatchSizeClass.Large;
        }

        return CpgWorkBatchSizeClass.Oversized;
    }
}
