using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// 位集位宇宙压缩（N → D）的契约测试。
/// 位集只会被定义节点置位（`DataFlowPass` 中唯一的置位语句位于定义节点分支内），
/// 因此每个位集的宽度应由 `DefinitionCount` 决定，而不是 `FlowNodeCount`。
/// </summary>
public sealed class NLCPGDataFlowBitsetCompactionTests
{
    // 流节点多、定义少：96 个字面量与大量二元运算，定义只有 seed 参数。
    // 目标是让 N 明显超过 64（从而 ceil(N/64) > 1），而 D 仍为 1。
    private const string WideFlowNarrowDefinitionSource = """
      namespace Demo;

      public sealed class WideFlowSample
      {
        public int Compute(int seed)
        {
          return seed
            + 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8
            + 9 + 10 + 11 + 12 + 13 + 14 + 15 + 16
            + 17 + 18 + 19 + 20 + 21 + 22 + 23 + 24
            + 25 + 26 + 27 + 28 + 29 + 30 + 31 + 32
            + 33 + 34 + 35 + 36 + 37 + 38 + 39 + 40
            + 41 + 42 + 43 + 44 + 45 + 46 + 47 + 48
            + 49 + 50 + 51 + 52 + 53 + 54 + 55 + 56
            + 57 + 58 + 59 + 60 + 61 + 62 + 63 + 64
            + 65 + 66 + 67 + 68 + 69 + 70 + 71 + 72
            + 73 + 74 + 75 + 76 + 77 + 78 + 79 + 80
            + 81 + 82 + 83 + 84 + 85 + 86 + 87 + 88
            + 89 + 90 + 91 + 92 + 93 + 94 + 95 + 96;
        }
      }
      """;

    // 无参数、无赋值、无调用、无属性引用 ⇒ 定义数为 0，但流节点数仍大于 0。
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

    [Fact]
    public void WordsPerSet_TracksDefinitionCount_NotFlowNodeCount()
    {
        var builder = new NLCPGBuilder(CreateOptions());

        _ = builder.BuildFromSource(WideFlowNarrowDefinitionSource, "bitset-compaction-width.cs");

        var metrics = Assert.Single(builder.LastBuildMetrics.DataFlowMethodMetrics!);
        Assert.True(
          metrics.FlowNodeCount > metrics.DefinitionCount,
          $"预期流节点数大于定义数，实际 N={metrics.FlowNodeCount} D={metrics.DefinitionCount}");
        Assert.Equal((metrics.DefinitionCount + 63) / 64, metrics.WordsPerSet);
        Assert.True(
          metrics.WordsPerSet < (metrics.FlowNodeCount + 63) / 64,
          $"位宽应按 D 计算；实际 wordsPerSet={metrics.WordsPerSet}，ceil(N/64)={(metrics.FlowNodeCount + 63) / 64}");
    }

    [Fact]
    public void WordsPerSet_WhenMethodHasNoDefinitions_StaysAtOneWord()
    {
        var builder = new NLCPGBuilder(CreateOptions());

        _ = builder.BuildFromSource(NoDefinitionSource, "bitset-compaction-nodef.cs");

        var metrics = Assert.Single(builder.LastBuildMetrics.DataFlowMethodMetrics!);
        Assert.Equal(0, metrics.DefinitionCount);
        Assert.True(metrics.FlowNodeCount > 0);
        // BitSetWordCount 的 Math.Max(1, ...) 语义：0 位仍需 1 个 word。
        Assert.Equal(1, metrics.WordsPerSet);
    }

    [Fact]
    public void DefinitionsReachingAcrossBlocks_KeepDataFlowEdges()
    {
        const string source = CpgBuilderSources.DataFlowFactCollection;
        var builder = new NLCPGBuilder(CreateOptions());

        var graph = builder.BuildFromSource(source, "bitset-compaction-cross-block.cs");

        var dataFlowEdges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.DataFlow)
          .ToArray();
        Assert.NotEmpty(dataFlowEdges);
    }

    [Fact]
    public void Compaction_PreservesDataFlowEdgesAcrossDegreesOfParallelism()
    {
        const string filePath = "bitset-compaction-dop.cs";
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
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        };
    }
}
