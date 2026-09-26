using System.Reflection;
using System.Runtime.CompilerServices;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

/// <summary>
/// 小型、固定输入的边存储测量。对应设计文档
/// <c>docs/plans/2026-09-24-frozen-edge-projection-design.md</c> §1 的实测口径。
///
/// 只把**机器无关的托管布局**写成断言（与 <see cref="NLCPGNodeStoragePerformanceTests"/> 同约定：
/// 记录数据，不把机器相关的耗时阈值写成契约）。
/// </summary>
public sealed class NLCPGEdgeStoragePerformanceTests
{
  private const int NodeCount = 2_000;
  private const int EdgeCount = 50_000;
  private const string FilePath = "nlcpg-edge-storage-benchmark.cs";

  private readonly ITestOutputHelper _output;

  public NLCPGEdgeStoragePerformanceTests(ITestOutputHelper output)
  {
    _output = output;
  }

  // 验证设计文档 §1.1 的 72 B 逐字段分解。
  [Fact]
  public void EdgeCarrierWidths_MatchDocumentedLayout()
  {
    var edgeSize = Unsafe.SizeOf<NLCPGEdge>();
    var nodeSize = Unsafe.SizeOf<NLCPGNode>();
    var nodeIdSize = Unsafe.SizeOf<NodeId>();
    var contextIdSize = Unsafe.SizeOf<NLCPGContextId?>();
    var callSiteSize = Unsafe.SizeOf<NLCPGCallSiteContext?>();
    var labelRefSize = Unsafe.SizeOf<NLCPGEdgeLabel?>();

    _output.WriteLine(
      $"runtime={Environment.Version}; arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; " +
      $"sizeof-NLCPGEdge={edgeSize}; sizeof-NLCPGNode={nodeSize}; sizeof-NodeId={nodeIdSize}; " +
      $"sizeof-NLCPGContextId?={contextIdSize}; sizeof-NLCPGCallSiteContext?={callSiteSize}; " +
      $"sizeof-NLCPGEdgeLabel?={labelRefSize}");

    // 设计文档 §1.1：4 + 4 + 4 + 8 + 16 + 32 = 72。
    Assert.Equal(72, edgeSize);
    Assert.Equal(4, nodeIdSize);
    Assert.Equal(16, contextIdSize);
    Assert.Equal(32, callSiteSize);
    Assert.Equal(8, labelRefSize);
  }

  // 报告冻结后常驻的边字节：实际常驻（SoA 存储 + 4 B/边插入序表）对比被替换掉的旧表示
  // （常驻 HashSet<NLCPGEdge> + 72 B/边 canonical 数组）。对应设计文档 §1.2 / §4.1
  // 与执行计划的阶段 3。
  //
  // 旧表示里的 HashSet 已随阶段 3 删除，故这里由投影出的边【重建】一个同元素 HashSet
  // 来测量其槽位容量——同一批元素、同一比较器，容量与旧代码实际持有的 HashSet 相同。
  [Fact]
  public void FrozenGraph_ReportsResidentEdgeBytes()
  {
    var graph = BuildGraph(metadataOnEveryEdge: false);
    var nodeTotal = graph.Nodes.Count;
    var edgeTotal = graph.Edges.Count;

    var edgeSize = Unsafe.SizeOf<NLCPGEdge>();
    var entrySlotSize = 8 + edgeSize; // hashCode(4) + next(4) + value(72)
    var canonical = ReadCanonicalEdges(graph);
    var storeGeometry = ReadStoreGeometry(graph);
    var insertionOrderBytes = ReadInsertionOrderBytes(graph);

    // 重建旧表示中的常驻 HashSet，以测量它会占用的槽位/桶。
    var legacySet = new HashSet<NLCPGEdge>(canonical);
    var setCapacity = ReadHashSetCapacity(legacySet);

    var setBytes = (long)setCapacity.SlotCount * entrySlotSize;
    var bucketBytes = (long)setCapacity.BucketCount * sizeof(int);
    var legacyCanonicalBytes = 24L + ((long)canonical.Length * edgeSize);
    var storeBytes = storeGeometry.Bytes;
    var informationBytes = (long)canonical.Length * 12; // src(4) + tgt(4) + kind(4)

    var residentNow = storeBytes + insertionOrderBytes;
    var residentLegacy = setBytes + bucketBytes + legacyCanonicalBytes;
    var reduction = residentLegacy == 0
      ? 0d
      : 100d * (residentLegacy - residentNow) / residentLegacy;
    var canonicalReduction = 100d * (legacyCanonicalBytes - storeBytes) / legacyCanonicalBytes;

    _output.WriteLine(
      $"edges={canonical.Length}; nodes={nodeTotal}; edges-via-api={edgeTotal}; " +
      $"legacy-hashset-slots={setCapacity.SlotCount}; legacy-hashset-buckets={setCapacity.BucketCount}; " +
      $"entry-slot-bytes={entrySlotSize}; " +
      $"legacy-set-bytes={setBytes}; legacy-bucket-bytes={bucketBytes}; " +
      $"store-bytes={storeBytes}; store-bytes-per-edge={storeGeometry.BytesPerEdge:F2}; " +
      $"store-geometry={storeGeometry.Description}; " +
      $"insertion-order-bytes={insertionOrderBytes}; " +
      $"resident-now-bytes={residentNow}; resident-legacy-bytes={residentLegacy}; " +
      $"legacy-canonical-bytes={legacyCanonicalBytes}; " +
      $"canonical-reduction-percent={canonicalReduction:F2}; " +
      $"resident-reduction-percent={reduction:F2}; " +
      $"information-bytes={informationBytes}");

    Assert.True(canonical.Length > 0, "expected a non-empty frozen edge set");
    Assert.Equal(edgeTotal, canonical.Length);

    // 存储侧的每边宽度必须远低于被替换掉的 72 B/边（设计文档 §4.1）。
    Assert.True(
      storeGeometry.BytesPerEdge <= 16d,
      $"expected <=16 B/edge in the canonical store, got {storeGeometry.BytesPerEdge:F2}");
    Assert.True(
      storeGeometry.BytesPerEdge >= 9d,
      $"expected >=9 B/edge (three key columns), got {storeGeometry.BytesPerEdge:F2}");

    // canonical 常驻分量本身的降幅（设计文档 §4.1 的正面目标）。
    Assert.True(
      canonicalReduction >= 80d,
      $"expected >=80% reduction of the canonical edge component, got {canonicalReduction:F2}%");

    // 阶段 3 完成后，整体常驻边字节的降幅必须显著高于阶段的 1+2 的 26%：
    // 现在两侧都换成了按边计的紧凑数组（9 B/边 + 4 B/边），而旧表示是
    // HashSet（80 B/槽，含装填因子放大）+ 72 B/边 canonical 数组。
    Assert.True(
      reduction >= 80d,
      $"expected >=80% overall resident reduction after stage 3, got {reduction:F2}%");
  }

  // 元数据填充与否不改变 NLCPGEdge 的宽度（定宽值类型），故旧表示下常驻字节不变。
  // 这是设计文档 §1.1「56 B = 77.8% 为元数据位」的因果验证；
  // 同时也验证新的 SoA 存储在元数据存在时才分配元数据侧表。
  [Fact]
  public void MetadataPresence_DoesNotChangeResidentWidth()
  {
    var graphWithout = BuildGraph(metadataOnEveryEdge: false);
    var graphWith = BuildGraph(metadataOnEveryEdge: true);
    var without = ReadCanonicalEdges(graphWithout);
    var with = ReadCanonicalEdges(graphWith);
    var geometryWithout = ReadStoreGeometry(graphWithout);
    var geometryWith = ReadStoreGeometry(graphWith);

    _output.WriteLine(
      $"edges-without-metadata={without.Length}; edges-with-metadata={with.Length}; " +
      $"sizeof-NLCPGEdge={Unsafe.SizeOf<NLCPGEdge>()}; " +
      $"legacy-canonical-bytes-without={24L + ((long)without.Length * Unsafe.SizeOf<NLCPGEdge>())}; " +
      $"legacy-canonical-bytes-with={24L + ((long)with.Length * Unsafe.SizeOf<NLCPGEdge>())}; " +
      $"store-bytes-per-edge-without={geometryWithout.BytesPerEdge:F2}; " +
      $"store-bytes-per-edge-with={geometryWith.BytesPerEdge:F2}; " +
      $"metadata-ids-without={geometryWithout.HasMetadata}; " +
      $"metadata-ids-with={geometryWith.HasMetadata}");

    Assert.Equal(without.Length, with.Length);
    // 旧表示下宽度与元数据无关（定宽值类型）。
    Assert.Equal(
      24L + ((long)without.Length * Unsafe.SizeOf<NLCPGEdge>()),
      24L + ((long)with.Length * Unsafe.SizeOf<NLCPGEdge>()));
    // 新表示下只有元数据存在时才分配元数据侧表。
    Assert.False(geometryWithout.HasMetadata);
    Assert.True(geometryWith.HasMetadata);
  }

  private static NLCPGGraph BuildGraph(bool metadataOnEveryEdge)
  {
    var graph = new NLCPGGraph();
    var nodes = new NLCPGNode[NodeCount];
    for (var index = 0; index < NodeCount; index += 1)
    {
      nodes[index] = graph.AddNode(new NLCPGNodeDraft(
        NLCPGNodeKind.Operation,
        Name: "node" + index,
        FilePath: FilePath,
        SpanStart: index * 10,
        SpanEnd: (index * 10) + 5));
    }

    var callSite = metadataOnEveryEdge
      ? new NLCPGCallSiteContext(FilePath, 0, 1, "bench")
      : (NLCPGCallSiteContext?)null;
    for (var index = 0; index < EdgeCount; index += 1)
    {
      var source = nodes[index % NodeCount];
      var target = nodes[(index * 7 + 1) % NodeCount];
      graph.AddEdge(
        source,
        target,
        (NLCPGEdgeKind)(index % 36),
        structuredLabel: null,
        contextId: null,
        callSiteContext: callSite);
    }

    graph.FreezeQueryIndex();
    return graph;
  }

  private readonly record struct StoreGeometry(
    long Bytes,
    double BytesPerEdge,
    bool HasMetadata,
    string Description);

  // 冻结后 Edges 的载体：插入序 -> canonical 序的 int[]（4 B/边）。
  private static long ReadInsertionOrderBytes(NLCPGGraph graph)
  {
    var index = ReadQueryIndex(graph);
    var order = (int[])index.GetType()
      .GetProperty("InsertionOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(index)!;
    return 24L + ((long)order.Length * sizeof(int));
  }

  private static (int SlotCount, int BucketCount) ReadHashSetCapacity(object set)
  {
    var type = set.GetType();
    var entries = type.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
      ?.GetValue(set) as Array;
    var buckets = type.GetField("_buckets", BindingFlags.Instance | BindingFlags.NonPublic)
      ?.GetValue(set) as Array;
    return (entries?.Length ?? 0, buckets?.Length ?? 0);
  }

  private static StoreGeometry ReadStoreGeometry(NLCPGGraph graph)
  {
    var index = ReadQueryIndex(graph);
    var store = index.GetType()
      .GetProperty("EdgeStore", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(index)!;
    var type = store.GetType();
    var count = (int)type.GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)!;
    var source = (int[])type.GetField("_sourceOrdinals", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)!;
    var target = (int[])type.GetField("_targetOrdinals", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)!;
    var kinds = (byte[])type.GetField("_kinds", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)!;
    var metadataIds = (int[]?)type.GetField("_metadataIds", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store);
    var metadataPool = type.GetField("_metadataPool", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store);
    var poolLength = metadataPool is Array poolArray ? poolArray.Length : 0;

    // 数组对象头 24 B + 元素。kind 列为 byte[]，其余为 int[]。
    var bytes = 24L + ((long)source.Length * sizeof(int))
      + 24L + ((long)target.Length * sizeof(int))
      + 24L + ((long)kinds.Length * sizeof(byte))
      + (metadataIds is null ? 0L : 24L + ((long)metadataIds.Length * sizeof(int)));
    var perEdge = count == 0 ? 0d : (double)bytes / count;
    var description =
      $"src:{source.Length},tgt:{target.Length},kind:{kinds.Length}," +
      $"meta:{(metadataIds?.Length ?? 0)},pool:{poolLength}";
    return new StoreGeometry(bytes, perEdge, metadataIds is not null, description);
  }

  private static NLCPGEdge[] ReadCanonicalEdges(NLCPGGraph graph)
  {
    var index = ReadQueryIndex(graph);
    var store = index.GetType()
      .GetProperty("EdgeStore", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(index)!;
    var count = (int)store.GetType()
      .GetProperty("Count", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(store)!;
    var projected = new NLCPGEdge[count];
    var project = store.GetType().GetMethod(
      "Project",
      BindingFlags.Instance | BindingFlags.NonPublic)!;
    for (var ordinal = 0; ordinal < count; ordinal += 1)
    {
      projected[ordinal] = (NLCPGEdge)project.Invoke(store, new object[] { ordinal })!;
    }

    return projected;
  }

  private static object ReadQueryIndex(NLCPGGraph graph)
  {
    return graph.GetType()
      .GetField("_queryIndex", BindingFlags.Instance | BindingFlags.NonPublic)
      ?.GetValue(graph)
      ?? throw new InvalidOperationException("Graph is not frozen (no _queryIndex).");
  }
}
