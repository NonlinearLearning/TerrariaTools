using NLCPG.Builder;
using NLISSN.Application.Performance;
using NLISSN.Core.Performance;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class NLCPGPerformanceFactMappingTests
{
  [Fact]
  public void Map_PreservesBuilderFactsWithoutExposingBuilderTypes()
  {
    var metrics = new NLCPGBuildMetrics(
      OperationNodeCacheHitCount: 2,
      OperationNodeCacheMissCount: 3,
      OperationRootCacheHitCount: 4,
      OperationRootCacheMissCount: 5,
      OperationInventoryCount: 6,
      NodeCount: 10,
      EdgeCount: 20,
      ElapsedMilliseconds: 42,
      AnchorDiscoveryAnchorCount: 7,
      AnchorDiscoveryElapsedMilliseconds: 8,
      PassElapsedMilliseconds: new Dictionary<string, long>
      {
        ["Syntax"] = 11,
        ["Operation"] = 12
      },
      PersistenceMetrics: NLCPGPersistenceMetrics.Empty with
      {
        RestoreAttempted = true,
        RestoreHit = true,
        RestoreElapsedMilliseconds = 13,
        PersistElapsedMilliseconds = 14,
        RestoredShardCount = 2,
        ReusedShardCount = 1
      },
      DataFlowMethodMetrics: new[]
      {
        new NLCPGDataFlowMethodMetrics(
          "M",
          3,
          4,
          5,
          6,
          7,
          8,
          NLCPGDataFlowOverflowReason.None,
          SourceFilePath: "file.cs")
      },
      AnchorDiscoveryPassElapsedMilliseconds: new Dictionary<string, long>
      {
        ["Anchor"] = 9
      });

    var facts = CpgPerformanceFactMapper.Map("file.cs", "source-hash", metrics);

    Assert.Equal("file.cs", facts.ItemId);
    Assert.Equal("source-hash", facts.SourceIdentity);
    Assert.Equal(42, facts.BuildElapsedMs);
    Assert.Equal(10, facts.NodeCount);
    Assert.Equal(20, facts.EdgeCount);
    Assert.Equal(new[] { "Operation", "Syntax" }, facts.PassSamples.Select(sample => sample.PassId));
    Assert.Equal(7, facts.AnchorDiscovery!.AnchorCount);
    Assert.Equal(8, facts.AnchorDiscovery.ElapsedMilliseconds);
    Assert.Equal(1, facts.Persistence!.ReusedShardCount);
    Assert.Equal(13, facts.Persistence.RestoreElapsedMs);
    Assert.Equal(14, facts.Persistence.PersistElapsedMs);
    Assert.Equal(2, facts.CacheCounters["operationNode.hit"]);
    Assert.Equal(1, facts.DataFlowMethodSamples.Count);
    Assert.Equal("None", facts.DataFlowMethodSamples[0].OverflowReason);
  }

  /// <summary>
  /// 多文件批次下，每个文件的条目**只能**带出属于它自己的 data-flow 方法集。
  /// </summary>
  /// <remarks>
  /// 这是本缺陷的回归锁：多文件构建只产出**一份** builder 级指标（整批合并），
  /// 旧实现在 mapper 里无条件全量透传，于是每个文件都报出整批方法集——
  /// 逐文件方法数/溢出原因全部失真，报告体积按「文件数 × 全局方法数」膨胀。
  /// 既有测试只播 1 条指标，规模为 1 时"全局"与"逐文件"无法区分，故锁不住。
  /// </remarks>
  [Fact]
  public void Map_ScopesDataFlowMethodSamplesToTheOwningFile()
  {
    var metrics = new NLCPGBuildMetrics(
      OperationNodeCacheHitCount: 0,
      OperationNodeCacheMissCount: 0,
      OperationRootCacheHitCount: 0,
      OperationRootCacheMissCount: 0,
      OperationInventoryCount: 0,
      NodeCount: 10,
      EdgeCount: 20,
      ElapsedMilliseconds: 42,
      DataFlowMethodMetrics: new[]
      {
        new NLCPGDataFlowMethodMetrics(
          "A.M", 1, 1, 1, 1, 1, 1, NLCPGDataFlowOverflowReason.None,
          SourceFilePath: "a.cs"),
        new NLCPGDataFlowMethodMetrics(
          "B.M", 2, 2, 2, 2, 2, 2, NLCPGDataFlowOverflowReason.None,
          SourceFilePath: "b.cs"),
        new NLCPGDataFlowMethodMetrics(
          "B.N", 3, 3, 3, 3, 3, 3, NLCPGDataFlowOverflowReason.None,
          SourceFilePath: "b.cs")
      });

    var aFacts = CpgPerformanceFactMapper.Map("a.cs", "source-a", metrics);
    var bFacts = CpgPerformanceFactMapper.Map("b.cs", "source-b", metrics);

    Assert.Equal(new[] { "A.M" }, aFacts.DataFlowMethodSamples.Select(sample => sample.MethodName));
    Assert.Equal(new[] { "B.M", "B.N" }, bFacts.DataFlowMethodSamples.Select(sample => sample.MethodName));
  }

  /// <summary>
  /// 来源路径不明的指标不归属任何文件（宁可少报，也不跨文件污染）。
  /// </summary>
  [Fact]
  public void Map_DoesNotAttributeSamplesWithNoSourceFilePath()
  {
    var metrics = new NLCPGBuildMetrics(
      OperationNodeCacheHitCount: 0,
      OperationNodeCacheMissCount: 0,
      OperationRootCacheHitCount: 0,
      OperationRootCacheMissCount: 0,
      OperationInventoryCount: 0,
      NodeCount: 10,
      EdgeCount: 20,
      ElapsedMilliseconds: 42,
      DataFlowMethodMetrics: new[]
      {
        new NLCPGDataFlowMethodMetrics(
          "orphan", 1, 1, 1, 1, 1, 1, NLCPGDataFlowOverflowReason.None)
      });

    var facts = CpgPerformanceFactMapper.Map("a.cs", "source-a", metrics);

    Assert.Empty(facts.DataFlowMethodSamples);
  }
}
