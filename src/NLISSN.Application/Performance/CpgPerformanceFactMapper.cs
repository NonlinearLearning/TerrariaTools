using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using NLCPG.Builder;
using NLISSN.Core.Performance;

namespace NLISSN.Application.Performance;

public static class CpgPerformanceFactMapper
{
  public static CpgPerformanceFacts Map(
    string itemId,
    string? sourceIdentity,
    NLCPGBuildMetrics metrics)
  {
    ArgumentNullException.ThrowIfNull(metrics);

    var passSamples = metrics.PassElapsedMilliseconds is null
      ? Array.Empty<CpgPassPerformanceFact>()
      : metrics.PassElapsedMilliseconds
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => new CpgPassPerformanceFact(entry.Key, entry.Value))
        .ToArray();
    var anchorPassSamples = metrics.AnchorDiscoveryPassElapsedMilliseconds is null
      ? Array.Empty<CpgPassPerformanceFact>()
      : metrics.AnchorDiscoveryPassElapsedMilliseconds
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => new CpgPassPerformanceFact(entry.Key, entry.Value))
        .ToArray();
    var cacheCounters = new Dictionary<string, long>(StringComparer.Ordinal)
    {
      ["operationNode.hit"] = metrics.OperationNodeCacheHitCount,
      ["operationNode.miss"] = metrics.OperationNodeCacheMissCount,
      ["operationRoot.hit"] = metrics.OperationRootCacheHitCount,
      ["operationRoot.miss"] = metrics.OperationRootCacheMissCount,
      ["operationInventory.count"] = metrics.OperationInventoryCount
    };
    var dataFlowMethodSamples = metrics.DataFlowMethodMetrics is null
      ? Array.Empty<CpgDataFlowMethodPerformanceFact>()
      : metrics.DataFlowMethodMetrics
        .Select(method => new CpgDataFlowMethodPerformanceFact(
          method.MethodName,
          method.FlowNodeCount,
          method.WordsPerSet,
          method.DefinitionCount,
          method.WorklistIterations,
          method.RawCandidateCount,
          method.UniqueCandidateCount,
          method.OverflowReason.ToString()))
        .OrderBy(method => method.MethodName, StringComparer.Ordinal)
        .ToArray();

    return new CpgPerformanceFacts(
      itemId,
      sourceIdentity,
      metrics.ElapsedMilliseconds,
      passSamples,
      new CpgAnchorDiscoveryFacts(
        metrics.AnchorDiscoveryAnchorCount,
        metrics.AnchorDiscoveryElapsedMilliseconds,
        anchorPassSamples),
      MapPersistence(metrics.PersistenceMetrics),
      cacheCounters,
      dataFlowMethodSamples,
      metrics.NodeCount,
      metrics.EdgeCount);
  }

  public static string ComputeSourceIdentity(string source)
  {
    ArgumentNullException.ThrowIfNull(source);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
  }

  private static CpgPersistencePerformanceFacts? MapPersistence(
    NLCPGPersistenceMetrics? metrics)
  {
    if (metrics is null)
    {
      return null;
    }

    return new CpgPersistencePerformanceFacts(
      metrics.RestoreAttempted,
      metrics.RestoreHit,
      metrics.RestoreElapsedMilliseconds,
      metrics.CatalogReadMilliseconds,
      metrics.ShardReadMilliseconds,
      metrics.RestoredShardCount,
      metrics.RestoredShardBytes,
      metrics.PersistElapsedMilliseconds,
      metrics.FileWriteMilliseconds,
      metrics.CatalogWriteMilliseconds,
      metrics.RoutingIndexWriteMilliseconds,
      metrics.PrimaryShardCount,
      metrics.PrimaryShardBytes,
      metrics.BoundaryAdjacencyShardCount,
      metrics.BoundaryAdjacencyShardBytes,
      metrics.BoundaryEdgeCount,
      metrics.ReusedShardCount,
      metrics.ReuseMissCount,
      metrics.ReuseRejectedCount,
      metrics.ReusedShardBytes,
      metrics.PeakConcurrentFileWrites,
      metrics.PeakConcurrentShardExports,
      metrics.PeakReorderBuffer,
      metrics.PeakBufferedBoundaryEdges,
      metrics.CatalogBatchCount,
      metrics.CatalogRowCount,
      metrics.Provenance is null
        ? null
        : string.Join(
          "|",
          metrics.Provenance.SourceHash,
          metrics.Provenance.ProfileHash,
          metrics.Provenance.SchemaVersion,
          metrics.Provenance.CompilerIdentity,
          metrics.Provenance.CapabilityFingerprint));
  }
}
