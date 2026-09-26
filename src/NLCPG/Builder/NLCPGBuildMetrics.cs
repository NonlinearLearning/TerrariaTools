using System.Text.Json.Serialization;

namespace NLCPG.Builder;

public sealed record CpgPersistenceProvenance(
  string SourceHash,
  string ProfileHash,
  int SchemaVersion,
  string CompilerIdentity,
  string CapabilityFingerprint);

public sealed record NLCPGPersistenceMetrics(
  bool RestoreAttempted,
  bool RestoreHit,
  long RestoreElapsedMilliseconds,
  long CatalogReadMilliseconds,
  long ShardReadMilliseconds,
  int RestoredShardCount,
  long RestoredShardBytes,
  long PersistElapsedMilliseconds,
  long FileWriteMilliseconds,
  long CatalogWriteMilliseconds,
  long RoutingIndexWriteMilliseconds,
  int PrimaryShardCount,
  long PrimaryShardBytes,
  int BoundaryAdjacencyShardCount,
  long BoundaryAdjacencyShardBytes,
  long BoundaryEdgeCount,
  int ReusedShardCount,
  int ReuseMissCount,
  int ReuseRejectedCount,
  long ReusedShardBytes,
  int PeakConcurrentFileWrites,
  int PeakConcurrentShardExports,
  int PeakReorderBuffer,
  int PeakBufferedBoundaryEdges,
  int CatalogBatchCount,
  long CatalogRowCount,
  CpgPersistenceProvenance? Provenance = null)
{
    public long RestoreFactsElapsedMilliseconds { get; init; }

    public long RestoreFactsAllocatedBytes { get; init; }

    public long RestoreGraphImportElapsedMilliseconds { get; init; }

    public long RestoreGraphImportAllocatedBytes { get; init; }

    public static NLCPGPersistenceMetrics Empty { get; } = new(
      RestoreAttempted: false,
      RestoreHit: false,
      RestoreElapsedMilliseconds: 0,
      CatalogReadMilliseconds: 0,
      ShardReadMilliseconds: 0,
      RestoredShardCount: 0,
      RestoredShardBytes: 0,
      PersistElapsedMilliseconds: 0,
      FileWriteMilliseconds: 0,
      CatalogWriteMilliseconds: 0,
      RoutingIndexWriteMilliseconds: 0,
      PrimaryShardCount: 0,
      PrimaryShardBytes: 0,
      BoundaryAdjacencyShardCount: 0,
      BoundaryAdjacencyShardBytes: 0,
      BoundaryEdgeCount: 0,
      ReusedShardCount: 0,
      ReuseMissCount: 0,
      ReuseRejectedCount: 0,
      ReusedShardBytes: 0,
      PeakConcurrentFileWrites: 0,
      PeakConcurrentShardExports: 0,
      PeakReorderBuffer: 0,
      PeakBufferedBoundaryEdges: 0,
      CatalogBatchCount: 0,
      CatalogRowCount: 0);

    public int ReuseAttemptCount => ReusedShardCount + ReuseMissCount + ReuseRejectedCount;

    public double ReuseHitRate => ReuseAttemptCount == 0
      ? 0
      : (double)ReusedShardCount / ReuseAttemptCount;
}

public sealed record NLCPGBuildMetrics(
  int OperationNodeCacheHitCount,
  int OperationNodeCacheMissCount,
  int OperationRootCacheHitCount,
  int OperationRootCacheMissCount,
  int OperationInventoryCount,
  int NodeCount,
  int EdgeCount,
  long ElapsedMilliseconds,
  int AnchorDiscoveryAnchorCount = 0,
  long AnchorDiscoveryElapsedMilliseconds = 0,
  IReadOnlyDictionary<string, long>? PassElapsedMilliseconds = null,
  NLCPGPersistenceMetrics? PersistenceMetrics = null,
  IReadOnlyList<NLCPGDataFlowMethodMetrics>? DataFlowMethodMetrics = null,
  CpgBuildInventoryMetrics? BuildInventoryMetrics = null,
  IReadOnlyDictionary<string, long>? AnchorDiscoveryPassElapsedMilliseconds = null,
  int DataFlowBatchCount = 0,
  int DataFlowWorkerCount = 0,
  int CallGraphBatchCount = 0,
  bool InterproceduralBarrierCompleted = false,
  IReadOnlyList<CpgWorkBatchPerformanceEvent>? WorkBatchPerformanceEvents = null)
{
    public static NLCPGBuildMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// 单个方法的 data-flow 求解指标。
/// </summary>
/// <param name="WordsPerSet">
/// **逻辑位宇宙宽度** <c>ceil(DefinitionCount / 64)</c>，最小值 1。
/// <para>
/// ⚠️ <b>自 2026-09-23 位集稀疏化起，本字段不再反映实际存储量。</b>
/// 集合表示已从"每节点一个定宽稠密位集"改为"每节点一个内联稀疏集合"
/// （<c>N × (4 + 4k)</c> 字节，<c>k = 1</c>），实际存储与 <c>DefinitionCount</c> 无关。
/// 本字段保留为**逻辑量**，以便 <c>SchemaVersion</c> 与既有落盘断言不变；
/// <b>不得</b>再用它推算内存（旧口径 <c>N × WordsPerSet × 8</c> 已失效）。
/// </para>
/// </param>
/// <param name="SparseOverflowNodeCount">
/// 内联槽位溢出的流节点数（仅诊断/测试用）。**不映射**到
/// <c>CpgPerformanceFactMapper</c> / <c>PerformanceSummaryDocument</c>，不触碰持久化格式。
/// </param>
/// <param name="SourceFilePath">
/// 该指标所属的源文件路径，由 <c>DataFlowPass</c> 在归并进 builder 级列表时按
/// <c>CpgWorkItem.SourceFilePath</c> 填入。
/// <para>
/// ⚠ 多文件构建下 builder 级列表是**整批合并**的（一次 <c>Build</c> 只有一份指标），
/// 故必须靠本字段做逐项归属；否则每个文件的性能条目都会带出整批方法集。
/// 与 <c>CpgPerformanceFacts.ItemId</c> 同域，按序数比较。
/// </para>
/// <para>
/// <b>刻意不序列化（<see cref="JsonIgnoreAttribute"/>）：</b>本字段是**归属键**而非观测量。
/// 把它写进序列化指纹会让指纹随检出路径/工作目录变化——这正是
/// <c>DataFlowPlanConstructionReuseTests</c> 刻意避开 <c>DataFlowGraphSnapshot</c> 的原因。
/// 故本字段不参与任何序列化指纹，加入它不改变既有冻结口径。
/// </para>
/// </param>
public sealed record NLCPGDataFlowMethodMetrics(
  string MethodName,
  int FlowNodeCount,
  int WordsPerSet,
  int DefinitionCount,
  int WorklistIterations,
  int RawCandidateCount,
  int UniqueCandidateCount,
  NLCPGDataFlowOverflowReason OverflowReason,
  int SparseOverflowNodeCount = 0,
  [property: JsonIgnore] string? SourceFilePath = null);
