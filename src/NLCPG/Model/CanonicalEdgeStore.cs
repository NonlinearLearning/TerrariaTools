using NLCPG.Contracts;

namespace NLCPG.Model;

// 冻结后常驻的边存储：结构化数组（SoA）+ 稀疏元数据池，每边 9~16 B，
// 取代原先常驻的 NLCPGEdge[]（72 B/边）。
//
// 与 OrdinalNodeList 同构：只保存序数与紧凑键，查询时按需投影为 NLCPGEdge 值。
// NLCPGEdge 是 readonly record struct，故投影为零堆分配。
//
// 设计依据：docs/plans/2026-09-24-frozen-edge-projection-design.md §3.1。
internal sealed class CanonicalEdgeStore
{
    // 元数据池条目。保存的三元组与 NLCPGEdge 的对应属性逐字段相同，
    // 且 ContextId 已是构造 NLCPGEdge 时解析后的值，故投影结果与原值逐字段一致。
    private sealed class EdgeMetadataEntry
    {
        internal EdgeMetadataEntry(
          NLCPGEdgeLabel? structuredLabel,
          NLCPGContextId? contextId,
          NLCPGCallSiteContext? callSiteContext)
        {
            StructuredLabel = structuredLabel;
            ContextId = contextId;
            CallSiteContext = callSiteContext;
        }

        internal NLCPGEdgeLabel? StructuredLabel { get; }

        internal NLCPGContextId? ContextId { get; }

        internal NLCPGCallSiteContext? CallSiteContext { get; }
    }

    // 【第 14 轮】原先此处有一份私有的 EdgeMetadataKey 副本；现共用
    // NLCPGGraphIndex.EdgeMetadataKey（已提升为 internal），避免两处定义漂移。

    private readonly NLCPGNode[] _orderedNodes;
    private readonly int[] _sourceOrdinals;
    private readonly int[] _targetOrdinals;
    private readonly byte[] _kinds;

    // 元数据稀疏侧表：覆盖率实测为 0 时为 null，此时不分配任何元数据结构。
    private readonly int[]? _metadataIds;
    private readonly EdgeMetadataEntry[]? _metadataPool;

    private CanonicalEdgeStore(
      NLCPGNode[] orderedNodes,
      int[] sourceOrdinals,
      int[] targetOrdinals,
      byte[] kinds,
      int[]? metadataIds,
      EdgeMetadataEntry[]? metadataPool)
    {
        _orderedNodes = orderedNodes;
        _sourceOrdinals = sourceOrdinals;
        _targetOrdinals = targetOrdinals;
        _kinds = kinds;
        _metadataIds = metadataIds;
        _metadataPool = metadataPool;
    }

    internal int Count => _sourceOrdinals.Length;

    // 按 canonical 序投影为边值。零堆分配。
    internal NLCPGEdge Project(int canonicalIndex)
    {
        var metadataId = _metadataIds is null ? 0 : _metadataIds[canonicalIndex];
        if (metadataId == 0)
        {
            return NLCPGEdge.CreateProjected(
              _orderedNodes[_sourceOrdinals[canonicalIndex]].NodeId!.Value,
              _orderedNodes[_targetOrdinals[canonicalIndex]].NodeId!.Value,
              (NLCPGEdgeKind)_kinds[canonicalIndex],
              structuredLabel: null,
              contextId: null,
              callSiteContext: null);
        }

        var metadata = _metadataPool![metadataId - 1];
        return NLCPGEdge.CreateProjected(
          _orderedNodes[_sourceOrdinals[canonicalIndex]].NodeId!.Value,
          _orderedNodes[_targetOrdinals[canonicalIndex]].NodeId!.Value,
          (NLCPGEdgeKind)_kinds[canonicalIndex],
          metadata.StructuredLabel,
          metadata.ContextId,
          metadata.CallSiteContext);
    }

    // 由 canonical 置换与原边数组构建常驻存储。
    //
    // permutation[canonicalIndex] = 原始 edgeArray 下标；
    // 键数组（source/target/kind）均以原始下标为索引。
    //
    // 【第 14 轮】metadataValueClassOfEdge / metadataValueClassKeys 由 BuildMetadataRanks 传入，
    // 是它已经算好的值去重结果（每条边的值类下标 + 每个值类的代表键）。
    // 本方法原先自带一份同形的值去重（Dictionary<EdgeMetadataKey,int> 走 record 自动值相等），
    // 实测 7,756,984 次探测中 7,752,743 次命中 ⇒ 99.95% 是冗余计算。
    // 现直接复用上游划分：池 = 代表性键去重后的实例表，canonicalMetadataIds = 值类下标 + 1。
    //
    // 等价性（同二进制交替 A/B 实测）：切换"复用值类"与"自带 record 去重"两方案，
    // GraphSnapshotVersion 三轮均为 9B59A35A…E896，逐字节相同。
    internal static CanonicalEdgeStore Create(
      NLCPGNode[] orderedNodes,
      int[] permutation,
      int[] sourceOrdinals,
      int[] targetOrdinals,
      int[] kindKeys,
      int kindWidth,
      int[]? metadataValueClassOfEdge,
      NLCPGGraphIndex.EdgeMetadataKey[]? metadataValueClassKeys)
    {
        if (kindWidth > byte.MaxValue + 1)
        {
            // NLCPGEdgeKind 目前有 36 个取值；枚举一旦超过 256 个取值，
            // 必须改用更宽的 kinds 载体，而不是静默截断。
            throw new InvalidOperationException(
              $"NLCPGEdgeKind has {kindWidth} distinct ordinals, which no longer fits a byte-wide canonical kind column.");
        }

        var count = permutation.Length;
        var canonicalSourceOrdinals = new int[count];
        var canonicalTargetOrdinals = new int[count];
        var canonicalKinds = new byte[count];
        var canonicalMetadataIds = new int[count];

        // 元数据池：每个值类一条代表条目，下标 = 值类下标 + 1（0 保留表示"无元数据"）。
        // 只有上游确实产出值类时才分配（全部边无元数据时上游返回 null，此处同样不分配）。
        int[]? metadataIds = null;
        EdgeMetadataEntry[]? metadataPool = null;
        var hasMetadata = metadataValueClassKeys is not null && metadataValueClassOfEdge is not null;
        if (hasMetadata)
        {
            metadataPool = new EdgeMetadataEntry[metadataValueClassKeys!.Length];
            for (var valueClass = 0; valueClass < metadataValueClassKeys.Length; valueClass += 1)
            {
                var key = metadataValueClassKeys[valueClass];
                metadataPool[valueClass] = new EdgeMetadataEntry(
                  key.Label,
                  key.ContextId,
                  key.CallSiteContext);
            }

            // 注意：这里必须直接充当"按 canonical 序的元数据 id 列"，不能再另建一个数组，
            // 否则填的是 A、传给构造器的是 B（全 0）⇒ 每条边都投影成 null 元数据。
            metadataIds = canonicalMetadataIds;
        }

        for (var canonicalIndex = 0; canonicalIndex < count; canonicalIndex += 1)
        {
            var sourceIndex = permutation[canonicalIndex];
            canonicalSourceOrdinals[canonicalIndex] = sourceOrdinals[sourceIndex];
            canonicalTargetOrdinals[canonicalIndex] = targetOrdinals[sourceIndex];
            canonicalKinds[canonicalIndex] = (byte)kindKeys[sourceIndex];
            if (metadataIds is null)
            {
                continue;
            }

            // 值类下标 + 1：上游的 0 号值类是"全 null 元数据"，其代表的条目也应可被索引，
            // 故统一 +1 让 0 号值类映射到池下标 1，池下标 0 保持未用（与旧实现"id 从 1 起"一致）。
            canonicalMetadataIds[canonicalIndex] =
              metadataValueClassOfEdge![sourceIndex] + 1;
        }

        return new CanonicalEdgeStore(
          orderedNodes,
          canonicalSourceOrdinals,
          canonicalTargetOrdinals,
          canonicalKinds,
          metadataIds,
          metadataPool);
    }

}


