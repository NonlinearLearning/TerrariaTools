using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// 位集稀疏化（内联稀疏集合：<c>k=1</c> 个内联槽位 + lazy 溢出通道）的回归护栏测试。
///
/// 稀疏化是**纯内部表示变化**：它不引入任何外部可见的新行为，
/// 因此本类测试**不是** RED→GREEN 驱动，而是保证重构不破坏语义的护栏。
/// 真正能捕获"编码写错"的手段是稠密↔稀疏的差分验证（见执行计划 Task 5）。
///
/// 关键约束：<c>src/NLCPG</c> 没有 <c>InternalsVisibleTo</c>，故只能经
/// <c>NLCPGBuilder</c> 公开 API（图快照 + <c>LastBuildMetrics</c>）观测。
/// </summary>
public sealed class NLCPGDataFlowSparseSetTests
{
    /// <summary>
    /// 空集是合法且高频状态（实测 93.9% 的 out-set 为空），<c>count == 0</c> 必须可表示。
    /// </summary>
    private const string NoDefinitionSource = """
      namespace Demo;

      public sealed class NoDefinitionSample
      {
        public int Constant()
        {
          return 1 + 2;
        }
      }
      """;

    /// <summary>
    /// 溢出不截断的定向断言。
    ///
    /// **前置事实（已实测，1692 个真实方法 + 定向 fixture）**：
    /// out-set 基数由**同时活跃的参数定义**驱动，规律为
    /// <c>cardMax ≤ 参数个数</c>（1692/1692 成立），74.4% 的方法取等号。
    ///
    /// **反例警示（实测教训）**：单参数 + 多个局部变量的构造实测 <c>cardMax == 1</c>，
    /// 不触发溢出，会让本测试退化为空测试。故 fixture 必须**多参数**。
    ///
    /// **为什么冻结精确边数而不是"edges ≥ D"**：实测 `edges ≥ D` 的余量为
    /// 9 和 4（`edges=16 vs D=7`），截断一个定义后仍会通过 ⇒ 该形式**捕获不到截断**。
    /// 精确值经 5 次重跑确认稳定（DOP=1，确定性）。
    /// </summary>
    [Theory]
    [InlineData(CpgBuilderSources.DataFlowSparseOverflowRefOut, "sparse-overflow-refout.cs", 7, 16)]
    [InlineData(CpgBuilderSources.DataFlowSparseOverflowValueParams, "sparse-overflow-valueparams.cs", 6, 10)]
    public void CardinalityExceedingInlineSlot_AllDefinitionsStillReachUseSite(
      string source,
      string filePath,
      int expectedDefinitionCount,
      int expectedDataFlowEdgeCount)
    {
        var builder = new NLCPGBuilder(CreateOptions());

        var graph = builder.BuildFromSource(source, filePath);

        var metrics = Assert.Single(builder.LastBuildMetrics.DataFlowMethodMetrics!);

        // 断言这个 fixture 真的越过了内联槽位上限（k=1 ⇒ 2 个元素即溢出）。
        // 若哪天事实前提变了，这条会失败并提醒我们测试已失去意义。
        Assert.True(
          metrics.DefinitionCount > 1,
          $"fixture 必须产生 > 1 个定义才能压到溢出通道，实际 {metrics.DefinitionCount}");

        // 冻结精确值：任何溢出截断都会改变边数（或使建图失败）。
        Assert.Equal(expectedDefinitionCount, metrics.DefinitionCount);
        var dataFlowEdges = DataFlowEdges(graph);
        Assert.Equal(expectedDataFlowEdgeCount, dataFlowEdges.Length);
    }

    [Fact]
    public void SetWithNoDefinitions_BuildsWithoutError_AndKeepsOneWordWidth()
    {
        var builder = new NLCPGBuilder(CreateOptions());

        _ = builder.BuildFromSource(NoDefinitionSource, "sparse-nodef.cs");

        var metrics = Assert.Single(builder.LastBuildMetrics.DataFlowMethodMetrics!);
        Assert.Equal(0, metrics.DefinitionCount);
        Assert.True(metrics.FlowNodeCount > 0);
        // BitSetWordCount 的 Math.Max(1, ...) 语义：0 位仍需 1 个 word。
        Assert.Equal(1, metrics.WordsPerSet);
    }

    [Fact]
    public void WordsPerSet_RemainsLogicalBitUniverseWidth_NotStorageWidth()
    {
        // 稀疏化后 WordsPerSet 仍是**逻辑位宇宙宽度** ceil(D/64)，
        // 不再反映实际存储量（实际存储为 N × (4 + 4k) 字节 + 溢出）。
        var builder = new NLCPGBuilder(CreateOptions());

        _ = builder.BuildFromSource(CpgBuilderSources.DataFlowFactCollection, "sparse-wordswidth.cs");

        var metrics = Assert.Single(builder.LastBuildMetrics.DataFlowMethodMetrics!);
        Assert.Equal((metrics.DefinitionCount + 63) / 64, metrics.WordsPerSet);
        Assert.True(metrics.DefinitionCount > 0);
    }

    [Fact]
    public void DefinitionsReachingAcrossBlocks_KeepDataFlowEdges()
    {
        var builder = new NLCPGBuilder(CreateOptions());

        var graph = builder.BuildFromSource(CpgBuilderSources.DataFlowFactCollection, "sparse-cross-block.cs");

        Assert.NotEmpty(DataFlowEdges(graph));
    }

    [Fact]
    public void SparseSet_PreservesGraphAcrossDegreesOfParallelism()
    {
        const string filePath = "sparse-dop.cs";
        var source = CpgBuilderSources.DataFlowFactCollection;
        var baseline = new NLCPGBuilder(CreateOptions(maxDegreeOfParallelism: 1))
          .BuildFromSource(source, filePath);

        foreach (var maxDegreeOfParallelism in new[] { 1, 4, 12, 16 })
        {
            var graph = new NLCPGBuilder(CreateOptions(maxDegreeOfParallelism))
              .BuildFromSource(source, filePath);

            Assert.Equal(DescribeDataFlowEdges(baseline), DescribeDataFlowEdges(graph));
        }
    }

    [Fact]
    public void CandidateBudget_LargeEnough_DoesNotTripOverflowPath()
    {
        // 守住"序数升序存储"：TryAdd 在去重**之前**就以 RawCandidateCount 比预算，
        // 触顶会整方法丢弃全部 DataFlow 边。故枚举顺序改变会静默丢边。
        // 这里给足够大的预算，断言不走预算路径且图与无预算时一致。
        const string filePath = "sparse-budget-none.cs";
        var source = CpgBuilderSources.DataFlowSparseOverflowRefOut;
        var unbounded = new NLCPGBuilder(CreateOptions())
          .BuildFromSource(source, filePath);

        var budgetedOptions = CreateOptions() with
        {
            DataFlowOptions = new NLCPGDataFlowOptions(
              MaxDefinitionsPerMethod: int.MaxValue,
              MaxFlowNodesPerMethod: int.MaxValue,
              MaxCandidateEdgesPerMethod: 100_000)
        };
        var budgetedBuilder = new NLCPGBuilder(budgetedOptions);
        var budgeted = budgetedBuilder.BuildFromSource(source, filePath);

        var metrics = Assert.Single(budgetedBuilder.LastBuildMetrics.DataFlowMethodMetrics!);
        Assert.Equal(NLCPGDataFlowOverflowReason.None, metrics.OverflowReason);
        Assert.Equal(DescribeDataFlowEdges(unbounded), DescribeDataFlowEdges(budgeted));
    }

    private static string[] DataFlowEdges(NLCPGGraph graph)
    {
        return graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.DataFlow)
          .Select(edge => $"{edge.SourceNodeId}|{edge.TargetNodeId}")
          .OrderBy(text => text, StringComparer.Ordinal)
          .ToArray();
    }

    private static string[] DescribeDataFlowEdges(NLCPGGraph graph)
    {
        return graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.DataFlow)
          .Select(edge => $"{edge.SourceNodeId}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
          .OrderBy(text => text, StringComparer.Ordinal)
          .ToArray();
    }

    private static NLCPGBuilderOptions CreateOptions(int maxDegreeOfParallelism = 1)
    {
        return new NLCPGBuilderOptions(
          NLCPGBuilderMode.Partitioned,
          MaxDegreeOfParallelism: maxDegreeOfParallelism,
          LargeFileLineThreshold: 40,
          LargeFileMethodThreshold: 4,
          LargeMethodLineSpanThreshold: 6,
          SyntaxLargeFileLineThreshold: 40);
    }
}
