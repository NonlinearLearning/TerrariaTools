using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.Tests;

/// <summary>
/// **轴 A 契约**：把「装箱隔离策略」从「尺寸分类」中解耦后可得的装箱率提升。
///
/// 背景：历史上 <see cref="CpgWorkBatchBuilder"/> 用
/// <see cref="CpgWorkBatchCostModel.Classify"/> 的 <c>Large</c>/<c>Oversized</c>
/// 判定来决定「该方法是否独占一批」，而 <c>Large</c> 的下界就是
/// <c>MediumMaxCost = 200</c> ⇒ 隔离阈值被**硬绑**在 200，
/// 导致 <c>cost ∈ (200, 1500]</c> 的「中等偏大」方法各自独占一批、无法配对。
///
/// 解耦后新增 <see cref="CpgWorkBatchCostOptions.IsolationCostThreshold"/>，
/// 其**默认值仍等于 MediumMaxCost** ⇒ 既有行为逐字不变（本文件第一条用例守卫该点）。
/// </summary>
public sealed class CpgWorkBatchIsolationThresholdTests
{
    /// <summary>
    /// **兼容性守卫（最重要的一条）**：不显式设置隔离阈值时，它必须等于
    /// <c>MediumMaxCost</c>，从而与解耦前的 <c>Classify</c> 判定逐字等价。
    /// </summary>
    [Fact]
    public void IsolationCostThreshold_WhenNotSpecified_DefaultsToMediumMaxCost()
    {
        var options = new CpgWorkBatchCostOptions(
            smallMaxCost: 40,
            mediumMaxCost: 200,
            largeMaxCost: 800,
            targetBatchCost: 400,
            maxBatchCost: 800);

        Assert.Equal(200, options.MediumMaxCost);
        Assert.Equal(options.MediumMaxCost, options.IsolationCostThreshold);
        Assert.Equal(options.MediumMaxCost, CpgWorkBatchCostOptions.Default.IsolationCostThreshold);
    }

    /// <summary>
    /// 解耦前语义的**逐字复刻**：用默认阈值装箱，201 必须被隔离、800 被隔离、
    /// 801 被隔离；而 200 与 40 **不**被隔离。这条把旧 <c>Classify</c> 判定的
    /// 边界（严格大于 200）钉死。
    /// </summary>
    [Theory]
    [InlineData(40, false)]
    [InlineData(200, false)]
    [InlineData(201, true)]
    [InlineData(800, true)]
    [InlineData(801, true)]
    public void Build_WithDefaultThreshold_IsolatesExactlyAboveMediumMaxCost(int cost, bool isolated)
    {
        var items = new[]
        {
            CreateItem(0, cost),
            CreateItem(1, 10),
        };

        var batches = new CpgWorkBatchBuilder(CpgWorkBatchCostOptions.Default)
            .Build("sample.cs", items);

        var containsIsolated = batches.Any(batch => batch.Items.Count == 1 && batch.Items[0].EstimatedCost == cost);
        Assert.Equal(isolated, containsIsolated);
    }

    /// <summary>
    /// **轴 A 的核心能力**：把隔离阈值抬到 1500 后，
    /// <c>cost ∈ (200, 1500]</c> 的方法不再独占，可与小方法**配对装箱**。
    /// 这是「保留完整函数」约束下提高装箱率的唯一手段。
    /// </summary>
    [Fact]
    public void Build_WhenIsolationThresholdRaised_PairsMidSizedMethodsWithSmallOnes()
    {
        var options = new CpgWorkBatchCostOptions(
            smallMaxCost: 40,
            mediumMaxCost: 1500,
            largeMaxCost: 1550,
            targetBatchCost: 1500,
            maxBatchCost: 1500)
        {
            IsolationCostThreshold = 1500,
        };
        var items = new[]
        {
            CreateItem(0, 700),
            CreateItem(1, 700),
            CreateItem(2, 50),
        };

        var batches = new CpgWorkBatchBuilder(options).Build("sample.cs", items);

        // 700 + 700 + 50 = 1450 <= 1500 ⇒ 三项合成一批，而不是三个单项批。
        var batch = Assert.Single(batches);
        Assert.Equal(3, batch.Items.Count);
        Assert.Equal(1450, batch.EstimatedCost);
    }

    /// <summary>
    /// 放宽阈值**不得**让超过阈值的完整方法被切开或强行合并：
    /// 单个 1600 的方法在 1500 阈值下仍独占一批、跨度原封不动。
    /// （「原子单位是完整函数、超出标准允许超出」这一硬约束。）
    /// </summary>
    [Fact]
    public void Build_WhenMethodExceedsRaisedThreshold_StillIsolatesItWithoutSplitting()
    {
        var options = new CpgWorkBatchCostOptions(
            smallMaxCost: 40,
            mediumMaxCost: 1500,
            largeMaxCost: 1550,
            targetBatchCost: 1500,
            maxBatchCost: 1500)
        {
            IsolationCostThreshold = 1500,
        };
        var items = new[]
        {
            CreateItem(0, 1600),
            CreateItem(1, 10),
        };

        var batches = new CpgWorkBatchBuilder(options).Build("sample.cs", items);

        var isolated = Assert.Single(batches, batch => batch.Items.Any(item => item.EstimatedCost == 1600));
        var method = Assert.Single(isolated.Items);
        Assert.Equal(1600, method.EstimatedCost);
        Assert.Equal(1600, method.SpanEnd - method.SpanStart);
    }

    /// <summary>
    /// <see cref="CpgWorkBatchCostOptions.Packing"/> 预设在结构上必须自洽
    /// （构造函数的三条不变式），且其隔离阈值等于 1500。
    /// </summary>
    [Fact]
    public void PackingPreset_IsStructurallyValidAndUsesFifteenHundredThreshold()
    {
        var packing = CpgWorkBatchCostOptions.Packing;

        Assert.Equal(1500, packing.IsolationCostThreshold);
        Assert.True(packing.SmallMaxCost < packing.MediumMaxCost);
        Assert.True(packing.MediumMaxCost < packing.LargeMaxCost);
        Assert.True(packing.TargetBatchCost <= packing.MaxBatchCost);
    }

    /// <summary>
    /// <see cref="CpgWorkBatchCostOptions.Default"/> 必须**保持保守值不动**
    /// —— 两条既有 <c>CpgWorkBatchBuilderTests</c> 用例断言 201/801 被隔离，
    /// 且它是 <c>CpgWorkBatchCostModel.Estimate</c> 的默认参数。
    /// </summary>
    [Fact]
    public void DefaultPreset_KeepsConservativeIsolationUnchanged()
    {
        var def = CpgWorkBatchCostOptions.Default;

        Assert.Equal(40, def.SmallMaxCost);
        Assert.Equal(200, def.MediumMaxCost);
        Assert.Equal(800, def.LargeMaxCost);
        Assert.Equal(400, def.TargetBatchCost);
        Assert.Equal(800, def.MaxBatchCost);
        Assert.Equal(200, def.IsolationCostThreshold);
    }

    /// <summary>
    /// **生产接线守卫**：`NLCPGBuilderOptions` 未显式传 cost options 时，
    /// 必须采用放宽后的 <see cref="CpgWorkBatchCostOptions.Packing"/>，
    /// 否则轴 A 的收益不会出现在真实构建里。
    /// </summary>
    [Fact]
    public void BuilderOptions_WhenCostOptionsNotProvided_UsesPackingPreset()
    {
        var options = NLCPGBuilderOptions.CreateDefault();

        Assert.Equal(1500, options.EffectiveWorkBatchCostOptions.IsolationCostThreshold);
    }

    /// <summary>
    /// 显式传入的 cost options 必须被尊重（不得被预设覆盖）。
    /// </summary>
    [Fact]
    public void BuilderOptions_WhenCostOptionsProvided_HonorsExplicitValue()
    {
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            WorkBatchCostOptions = CpgWorkBatchCostOptions.Default,
        };

        Assert.Equal(200, options.EffectiveWorkBatchCostOptions.IsolationCostThreshold);
    }

    private static CpgWorkItem CreateItem(int stableOrder, int cost)
    {
        return new CpgWorkItem(
          stableOrder,
          "sample.cs",
          $"M:{stableOrder}",
          spanStart: stableOrder * 10_000,
          spanEnd: stableOrder * 10_000 + cost,
          estimatedCost: cost,
          kind: CpgWorkItemKind.Method);
    }
}
