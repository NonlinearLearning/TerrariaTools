using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// D1 / **T3** 契约：允许**跨文件**把多个文件的完整方法装箱到同一批，
/// 以便小/中对象凑到标准大小。
///
/// 不变量（必须与 T2 一起成立）：
/// <list type="bullet">
/// <item>原子单位是**完整方法**——绝不切开方法体；单方法超标准时允许其自身超批。</item>
/// <item><see cref="CpgWorkItem.StableOrder"/> 保持**文件内局部**语义（载荷下标），跨文件可重复。</item>
/// <item>去重键必须**带文件限定**，否则两个文件的同名方法会被静默丢弃。</item>
/// </list>
/// </summary>
public sealed class CpgWorkBatchCrossFilePackingTests
{
    /// <summary>
    /// T3 核心能力：来自**两个文件**的 work item 现在可以进入同一次 build，
    /// 且不再抛 "must use the requested source file path"。
    /// </summary>
    [Fact]
    public void Build_WhenItemsFromTwoFiles_DoesNotThrowAndKeepsEveryItem()
    {
        var items = new[]
        {
            CreateItem("a.cs", stableOrder: 0, cost: 30),
            CreateItem("b.cs", stableOrder: 0, cost: 30),
            CreateItem("a.cs", stableOrder: 1, cost: 30),
            CreateItem("b.cs", stableOrder: 1, cost: 30),
        };

        var batches = new CpgWorkBatchBuilder(TargetSixty()).Build("a.cs", items);

        // 四项全部保留（既没抛异常，也没被静默丢弃）。
        Assert.Equal(
          items.Select(item => item.StableSpanIdentity).OrderBy(text => text, StringComparer.Ordinal),
          batches.SelectMany(batch => batch.Items)
            .Select(item => item.StableSpanIdentity)
            .OrderBy(text => text, StringComparer.Ordinal));
    }

    /// <summary>
    /// T3 的真实缺陷守卫：两个文件各自声明**同名方法**（`MethodSymbolKey` 相同）时，
    /// 两者都必须保留。修 T3 之前 `seenMethodKeys` 用裸符号键，会静默丢掉一个。
    /// </summary>
    [Fact]
    public void Build_WhenSameMethodSymbolKeyInTwoFiles_KeepsBothItems()
    {
        const string sharedKey = "M:Shared.Name";
        var items = new[]
        {
            CreateItem("a.cs", stableOrder: 0, cost: 20, methodSymbolKey: sharedKey),
            CreateItem("b.cs", stableOrder: 0, cost: 20, methodSymbolKey: sharedKey),
        };

        var batches = new CpgWorkBatchBuilder(TargetSixty()).Build("a.cs", items);
        var kept = batches.SelectMany(batch => batch.Items).ToArray();

        Assert.Equal(2, kept.Length);
        Assert.Contains(kept, item => item.SourceFilePath == "a.cs");
        Assert.Contains(kept, item => item.SourceFilePath == "b.cs");
    }

    /// <summary>
    /// 去重仍然生效：**同一文件内**的完全重复项（同一 span 或同一方法键）必须只保留一个，
    /// 否则去重逻辑被整体删掉了。
    /// </summary>
    [Fact]
    public void Build_WhenDuplicateWithinSameFile_DeduplicatesOnce()
    {
        var items = new[]
        {
            CreateItem("a.cs", stableOrder: 0, cost: 20, methodSymbolKey: "M:Dup"),
            CreateItem("a.cs", stableOrder: 0, cost: 20, methodSymbolKey: "M:Dup"),
        };

        var batches = new CpgWorkBatchBuilder(TargetSixty()).Build("a.cs", items);

        Assert.Single(batches.SelectMany(batch => batch.Items));
    }

    /// <summary>
    /// D1 的目标本身：小对象**跨文件凑到标准大小**。
    /// 四个 30 行的项（target=120）应被装进一批，而不是按文件各成一批。
    /// </summary>
    [Fact]
    public void Build_WhenSmallItemsFromTwoFiles_CombinesThemToReachTargetCost()
    {
        var items = new[]
        {
            CreateItem("a.cs", stableOrder: 0, cost: 30),
            CreateItem("b.cs", stableOrder: 0, cost: 30),
            CreateItem("a.cs", stableOrder: 1, cost: 30),
            CreateItem("b.cs", stableOrder: 1, cost: 30),
        };

        var batches = new CpgWorkBatchBuilder(TargetOneTwenty()).Build("a.cs", items);

        var batch = Assert.Single(batches);
        Assert.Equal(120, batch.EstimatedCost);
        Assert.Equal(4, batch.Items.Count);
        // 一批内确实同时含有两个文件的项——这才是「跨文件凑标准大小」。
        Assert.Contains(batch.Items, item => item.SourceFilePath == "a.cs");
        Assert.Contains(batch.Items, item => item.SourceFilePath == "b.cs");
    }

    /// <summary>
    /// 判据 4 的**正向可测形式**（原计划的「不承诺消除超标方法」不可证伪）：
    /// 单个方法自身超标准时，它**仍被隔离为单项批**，且**不被切开**。
    /// 断言的是隔离行为，不是「它消失」。
    /// </summary>
    [Fact]
    public void Build_WhenOversizedMethodPresent_StillIsolatesIt()
    {
        var items = new[]
        {
            CreateItem("a.cs", stableOrder: 0, cost: 30),
            // cost > MediumMaxCost(200) ⇒ 进入 Large/Oversized 隔离路径。
            CreateItem("a.cs", stableOrder: 1, cost: 5_000),
            CreateItem("b.cs", stableOrder: 0, cost: 30),
        };

        var batches = new CpgWorkBatchBuilder(TargetOneTwenty()).Build("a.cs", items);

        var oversized = Assert.Single(
          batches.Where(batch => batch.EstimatedCost == 5_000));
        // 隔离：该批只含这一个方法。
        var item = Assert.Single(oversized.Items);
        // 不切开：其跨度与估算代价原封不动。
        Assert.Equal(5_000, item.EstimatedCost);
        Assert.Equal(5_000, item.SpanEnd - item.SpanStart);
        // 且它没有被跨文件合并掉。
        Assert.Equal("a.cs", item.SourceFilePath);
    }

    /// <summary>
    /// 跨文件排序必须**确定**：输入顺序无关，输出批内项序一致。
    /// 这是把文件路径提为排序主键的原因（否则 StableOrder 跨文件重复会给出不确定排列）。
    /// </summary>
    [Fact]
    public void Build_WhenMultiFileInputOrderReversed_ProducesIdenticalBatches()
    {
        var items = new[]
        {
            CreateItem("b.cs", stableOrder: 0, cost: 30),
            CreateItem("a.cs", stableOrder: 1, cost: 30),
            CreateItem("a.cs", stableOrder: 0, cost: 30),
            CreateItem("b.cs", stableOrder: 1, cost: 30),
        };
        var builder = new CpgWorkBatchBuilder(TargetOneTwenty());

        var forward = builder.Build("a.cs", items);
        var reverse = builder.Build("a.cs", items.Reverse());

        Assert.Equal(Describe(forward), Describe(reverse));
        // 文件路径为主键 ⇒ a.cs 的项先于 b.cs。
        Assert.Equal(new[] { "a.cs", "a.cs", "b.cs", "b.cs" },
          forward.SelectMany(batch => batch.Items).Select(item => item.SourceFilePath));
    }

    /// <summary>
    /// 单文件输入的**逐字兼容**：文件路径恒定 ⇒ 排序退化为原 `StableOrder` 序，
    /// 既有 6 个 `CpgWorkBatchBuilderTests` 的期望不变。
    /// </summary>
    [Fact]
    public void Build_WhenSingleFile_KeepsOriginalStableOrderPacking()
    {
        var items = new[]
        {
            CreateItem("sample.cs", stableOrder: 0, cost: 30),
            CreateItem("sample.cs", stableOrder: 1, cost: 30),
            CreateItem("sample.cs", stableOrder: 2, cost: 30),
            CreateItem("sample.cs", stableOrder: 3, cost: 30),
        };

        var batches = new CpgWorkBatchBuilder(TargetSixty()).Build("sample.cs", items);

        Assert.Equal(2, batches.Count);
        Assert.Equal(new[] { 0, 1 }, batches[0].Items.Select(item => item.StableOrder));
        Assert.Equal(new[] { 2, 3 }, batches[1].Items.Select(item => item.StableOrder));
    }

    private static CpgWorkBatchCostOptions TargetSixty() => new(
      smallMaxCost: 40,
      mediumMaxCost: 200,
      largeMaxCost: 800,
      targetBatchCost: 60,
      maxBatchCost: 400);

    private static CpgWorkBatchCostOptions TargetOneTwenty() => new(
      smallMaxCost: 40,
      mediumMaxCost: 200,
      largeMaxCost: 800,
      targetBatchCost: 120,
      maxBatchCost: 400);

    private static CpgWorkItem CreateItem(
      string sourceFilePath,
      int stableOrder,
      int cost,
      string? methodSymbolKey = null)
    {
        // span 必须随 stableOrder 变化，否则同文件同 cost 的项会撞上同一个
        // StableSpanIdentity 而被去重（这正是被测的去重键语义）。
        return new CpgWorkItem(
          stableOrder,
          sourceFilePath,
          methodSymbolKey ?? $"M:{sourceFilePath}:{stableOrder}",
          spanStart: stableOrder * 100,
          spanEnd: stableOrder * 100 + cost,
          estimatedCost: cost,
          kind: CpgWorkItemKind.Method);
    }

    private static IEnumerable<string> Describe(IReadOnlyList<CpgWorkBatch> batches)
    {
        return batches.Select(batch =>
          $"[{batch.EstimatedCost}]" + string.Join(
            "|",
            batch.Items.Select(item => item.StableSpanIdentity)));
    }
}
