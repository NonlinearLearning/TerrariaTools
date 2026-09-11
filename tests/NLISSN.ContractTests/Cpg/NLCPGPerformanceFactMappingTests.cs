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
          NLCPGDataFlowOverflowReason.None)
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
}
