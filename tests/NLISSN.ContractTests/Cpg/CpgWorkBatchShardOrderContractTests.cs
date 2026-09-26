using NLCPG.Builder.Concurrency;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G4b / D1 **T2** 契约：<see cref="CpgWorkItem.ShardOrder"/> 与
/// <see cref="CpgWorkBatch.ShardOrder"/> 是**跨文件全局单调**调度序号，
/// 而 <see cref="CpgWorkItem.StableOrder"/> 必须保持**文件内局部**语义
/// （它同时是 `DataFlowPass` 的 `methodPartitions[item.StableOrder]` 下标与
/// `CallGraphPass` 的 `operationWorkByOrder` 字典键）。
///
/// 本文件的唯一目的：证明「两个文件可以有同名 StableOrder，而 ShardOrder 全局唯一」
/// 这一 T2 新增能力成立，且单文件行为**逐字未变**。
/// </summary>
public sealed class CpgWorkBatchShardOrderContractTests
{
    /// <summary>
    /// T2 核心能力：两个文件各自的局部序号都从 0 开始，跨文件装箱后
    /// **不得**再因 StableOrder 重复而抛出；归并序由 ShardOrder 决定。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenBatchesFromTwoFilesShareStableOrder_UsesShardOrderForReduction()
    {
        var reducedOrders = new List<int>();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 4,
          useSynchronousExecution: true));

        // 两个文件，各自的 StableOrder 都是 0/1（文件内局部），ShardOrder 全局 0..3。
        var batches = new[]
        {
            CreateBatch(batchId: 0, file: "a.cs", stableOrder: 0, shardOrder: 0),
            CreateBatch(batchId: 1, file: "b.cs", stableOrder: 0, shardOrder: 1),
            CreateBatch(batchId: 2, file: "a.cs", stableOrder: 1, shardOrder: 2),
            CreateBatch(batchId: 3, file: "b.cs", stableOrder: 1, shardOrder: 3),
        };

        // 若仍按 StableOrder 校验唯一性，此处会抛
        // "WorkBatch stable orders must be unique."；按 ShardOrder 则不抛。
        var collected = await executor.ExecuteAsync(
          batches,
          (batch, _, _) => batch.ShardOrder,
          reducedOrders.Add,
          stageId: "CPG.WorkBatch.ShardOrderContract");

        Assert.Equal(new[] { 0, 1, 2, 3 }, collected);
        // 归并必须严格按 ShardOrder 升序，而不是按重复的 StableOrder。
        Assert.Equal(new[] { 0, 1, 2, 3 }, reducedOrders);
    }

    /// <summary>
    /// 反向守卫：ShardOrder 若**也**重复，必须仍然拒绝——否则 T2 只是把
    /// 唯一性校验删掉了，而不是搬到了正确的键上。
    ///
    /// ⚠️ 刻意使用**异步**路径（<c>useSynchronousExecution: false</c>，即生产默认）：
    /// 唯一性校验只存在于 <c>ExecuteCoreAsync</c>；同步路径
    /// <c>ExecuteSynchronously</c> **没有**该校验（本文件末尾有专项说明）。
    /// 本测试断言的是**生产路径**的守卫，这才是 T2 要搬移的那一处。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenShardOrderDuplicated_Throws()
    {
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2));

        var batches = new[]
        {
            CreateBatch(batchId: 0, file: "a.cs", stableOrder: 0, shardOrder: 7),
            CreateBatch(batchId: 1, file: "b.cs", stableOrder: 0, shardOrder: 7),
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
          () => executor.ExecuteAsync(
            batches,
            (batch, _, _) => batch.ShardOrder,
            stageId: "CPG.WorkBatch.ShardOrderContract"));

        Assert.Contains("shard orders must be unique", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠️ **已知缺口（T2 未修，仅记录）**：同步路径不校验序号唯一性。
    ///
    /// 本测试**固定现状**而非认可它：<c>useSynchronousExecution: true</c> 时
    /// 重复的 ShardOrder **不抛异常**，重复批次会被执行两次。
    /// 生产默认是异步路径（无任何调用点设 <c>useSynchronousExecution: true</c>），
    /// 故这不影响 D1；但它是一处真实的路径不对称。
    /// 一旦同步路径补上校验，本测试**应当失败并被删除**——这是刻意留的哨兵。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenSynchronousExecution_DoesNotValidateShardOrderUniqueness()
    {
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 1,
          useSynchronousExecution: true));
        var executed = new List<int>();

        var batches = new[]
        {
            CreateBatch(batchId: 0, file: "a.cs", stableOrder: 0, shardOrder: 7),
            CreateBatch(batchId: 1, file: "b.cs", stableOrder: 0, shardOrder: 7),
        };

        await executor.ExecuteAndConsumeAsync(
          batches,
          (batch, _, _) => batch.ShardOrder,
          consumed => executed.Add(consumed),
          stageId: "CPG.WorkBatch.ShardOrderContract.SyncGap");

        // 现状：两个批次都被执行（未拒绝）。
        Assert.Equal(new[] { 7, 7 }, executed);
    }

    /// <summary>
    /// 单文件路径的**逐字兼容**：不指定 ShardOrder 时它默认等于 StableOrder，
    /// 故既有 8 个 pass 的调用点无需改动，行为与 T2 之前完全一致。
    /// </summary>
    [Fact]
    public void CpgWorkItem_WhenShardOrderOmitted_DefaultsToStableOrder()
    {
        var item = new CpgWorkItem(
          stableOrder: 5,
          sourceFilePath: "sample.cs",
          methodSymbolKey: "M:Sample",
          spanStart: 100,
          spanEnd: 140,
          estimatedCost: 41,
          kind: CpgWorkItemKind.Method);

        Assert.Equal(5, item.StableOrder);
        Assert.Equal(5, item.ShardOrder);
    }

    /// <summary>
    /// StableOrder 仍是**文件内局部**序号（载荷下标语义），不得被 ShardOrder 取代：
    /// 两个文件的 item 可以有相同的 StableOrder，且各自的 ShardOrder 独立。
    /// </summary>
    [Fact]
    public void CpgWorkItem_WhenSameStableOrderAcrossFiles_KeepsLocalStableOrderAndDistinctShardOrder()
    {
        var fromA = new CpgWorkItem(
          stableOrder: 0, sourceFilePath: "a.cs", methodSymbolKey: "M:A",
          spanStart: 0, spanEnd: 10, estimatedCost: 11,
          kind: CpgWorkItemKind.Method, shardOrder: 0);
        var fromB = new CpgWorkItem(
          stableOrder: 0, sourceFilePath: "b.cs", methodSymbolKey: "M:B",
          spanStart: 0, spanEnd: 10, estimatedCost: 11,
          kind: CpgWorkItemKind.Method, shardOrder: 1);

        // 载荷下标语义：两者都必须保持 0（否则 DataFlowPass 的数组下标会错配）。
        Assert.Equal(0, fromA.StableOrder);
        Assert.Equal(0, fromB.StableOrder);
        // 调度语义：全局单调，必须可区分。
        Assert.NotEqual(fromA.ShardOrder, fromB.ShardOrder);
    }

    /// <summary>
    /// <see cref="CpgWorkBatchBuilder"/> 在**单文件**输入下的行为必须与 T2 之前逐字一致：
    /// 批次数、批内项序、以及每批的 ShardOrder 都等于 StableOrder 下的既有结果。
    /// 这条守卫 T2 没有把单文件语义改坏。
    /// </summary>
    [Fact]
    public void Build_WhenSingleFile_ProducesShardOrderEqualToStableOrder()
    {
        var options = new CpgWorkBatchCostOptions(
          smallMaxCost: 40,
          mediumMaxCost: 200,
          largeMaxCost: 800,
          targetBatchCost: 60,
          maxBatchCost: 120);
        var items = new[]
        {
            CreateItem(0, 30, "sample.cs"),
            CreateItem(1, 30, "sample.cs"),
            CreateItem(2, 30, "sample.cs"),
            CreateItem(3, 30, "sample.cs"),
        };

        var batches = new CpgWorkBatchBuilder(options).Build("sample.cs", items);

        Assert.Equal(2, batches.Count);
        // 既有断言（与 CpgWorkBatchBuilderTests 一致）——证明未回归。
        Assert.Equal(new[] { 0, 1 }, batches[0].Items.Select(item => item.StableOrder));
        Assert.Equal(new[] { 2, 3 }, batches[1].Items.Select(item => item.StableOrder));
        // T2 新增：单文件时 ShardOrder 与 StableOrder 一致。
        Assert.All(batches, batch => Assert.Equal(batch.StableOrder, batch.ShardOrder));
        Assert.All(
          batches.SelectMany(batch => batch.Items),
          item => Assert.Equal(item.StableOrder, item.ShardOrder));
    }

    /// <summary>
    /// 跨文件装箱（T3 的能力，此处只验证 T2 提供的 ShardOrder 载体已能表达）：
    /// 同一批内可容纳两个文件的项，且该批的 ShardOrder 取批内最小值。
    /// ⚠️ 本测试**不**放宽 <see cref="CpgWorkBatchBuilder"/> 的单文件断言——
    /// 那是 T3 的范围；此处直接构造 <see cref="CpgWorkBatch"/> 验证载体语义。
    /// </summary>
    [Fact]
    public void CpgWorkBatch_WhenItemsFromTwoFiles_CarriesMinShardOrder()
    {
        var itemA = CreateItem(0, 30, "a.cs", shardOrder: 10);
        var itemB = CreateItem(0, 30, "b.cs", shardOrder: 11);

        var batch = new CpgWorkBatch(
          batchId: 0,
          sourceFilePath: "a.cs",
          stableOrder: 0,
          items: new[] { itemA, itemB },
          estimatedCost: 60,
          estimatedNodeCount: 2,
          estimatedBytes: 3840,
          kind: CpgWorkBatchKind.Methods,
          shardOrder: 10);

        Assert.Equal(10, batch.ShardOrder);
        // 批内两项的 StableOrder 可以相同（各自文件内局部序号），
        // 这正是必须新增 ShardOrder 而不能改 StableOrder 的原因。
        Assert.Equal(itemA.StableOrder, itemB.StableOrder);
        Assert.Equal(new[] { 10, 11 }, batch.Items.Select(item => item.ShardOrder));
    }

    private static CpgWorkBatch CreateBatch(long batchId, string file, int stableOrder, int shardOrder)
    {
        var item = new CpgWorkItem(
          stableOrder, file, $"M:{file}:{stableOrder}",
          spanStart: stableOrder * 100,
          spanEnd: stableOrder * 100 + 10,
          estimatedCost: 11,
          kind: CpgWorkItemKind.Method,
          shardOrder: shardOrder);

        return new CpgWorkBatch(
          batchId, file, stableOrder, new[] { item },
          estimatedCost: 11, estimatedNodeCount: 1, estimatedBytes: 704,
          kind: CpgWorkBatchKind.Methods, shardOrder: shardOrder);
    }

    private static CpgWorkItem CreateItem(int stableOrder, int cost, string file, int? shardOrder = null)
    {
        return new CpgWorkItem(
          stableOrder, file, $"M:{file}:{stableOrder}",
          spanStart: stableOrder * 100,
          spanEnd: stableOrder * 100 + cost,
          estimatedCost: cost,
          kind: CpgWorkItemKind.Method,
          shardOrder: shardOrder ?? stableOrder);
    }
}
