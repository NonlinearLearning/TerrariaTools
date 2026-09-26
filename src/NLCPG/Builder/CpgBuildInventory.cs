using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder;

// 汇总一次构图已产生的只读事实计数，作为预分配和算法实验的共同审计基线。
internal sealed class CpgBuildInventory
{
    private const int AnchorSampleLimit = 8;

    // 锚点写入的定长步长：7 个 4 字节字段（Kind/FilePathId/SpanStart/SpanEnd/Role/Ordinal/ExtraKeyId）。
    private const int AnchorByteCount = 7 * sizeof(int);

    // 64 KiB：与 NLCPGGraphIndex.SnapshotHasher 同一容量口径：小到能常驻 L2，又远大于刷新开销。
    private const int FingerprintBufferCapacity = 64 * 1024;

    private CpgBuildInventory(CpgBuildInventoryMetrics metrics)
    {
        Metrics = metrics;
    }

    internal CpgBuildInventoryMetrics Metrics { get; }

    internal static CpgBuildInventory Create(NLCPGBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var nodes = context.Graph.Nodes;
        var edges = context.Graph.Edges;
        var syntaxFactCount = nodes.Count(node => node.Kind is NLCPGNodeKind.SyntaxNode or NLCPGNodeKind.SyntaxToken);
        var cfgFactCount = edges.Count(edge => edge.Kind is
          NLCPGEdgeKind.CfgNext or NLCPGEdgeKind.CfgTrue or NLCPGEdgeKind.CfgFalse);
        var typedSymbolFactCount = context.InvocationOperations.Count +
          context.PropertyReferenceOperations.Count +
          context.FieldReferenceOperations.Count +
          edges.Count(edge => edge.Kind is
            NLCPGEdgeKind.HasType or
            NLCPGEdgeKind.EvalType or
            NLCPGEdgeKind.ReferencesSymbol or
            NLCPGEdgeKind.RefersToType or
            NLCPGEdgeKind.OpResolvesToSymbol);
        var anchors = nodes
          .Select(node => node.StableAnchor ?? throw new InvalidOperationException(
            "A CPG build inventory requires stable node anchors."))
          .ToArray();

        return new CpgBuildInventory(new CpgBuildInventoryMetrics(
          SyntaxFactCount: syntaxFactCount,
          OperationFactCount: context.OperationInventory.Count,
          CfgFactCount: cfgFactCount,
          TypedSymbolFactCount: typedSymbolFactCount,
          AnchorCount: anchors.Length,
          AnchorFingerprint: Fingerprint(anchors)));
    }

    internal static CpgBuildAnchorDiffMetrics CompareAnchors(
      IReadOnlyCollection<StableNodeAnchor> expectedAnchors,
      IEnumerable<NLCPGNode> actualNodes)
    {
        ArgumentNullException.ThrowIfNull(expectedAnchors);
        ArgumentNullException.ThrowIfNull(actualNodes);
        var actualAnchors = actualNodes
          .Select(node => node.StableAnchor ?? throw new InvalidOperationException(
            "Anchor comparison requires stable node anchors."))
          .ToHashSet();
        var expectedSet = expectedAnchors.ToHashSet();
        var missing = OrderAnchors(expectedSet.Except(actualAnchors));
        var unexpected = OrderAnchors(actualAnchors.Except(expectedSet));
        return new CpgBuildAnchorDiffMetrics(
          ExpectedAnchorCount: expectedSet.Count,
          ActualAnchorCount: actualAnchors.Count,
          MissingAnchorCount: missing.Count,
          UnexpectedAnchorCount: unexpected.Count,
          ExpectedFingerprint: Fingerprint(expectedSet),
          ActualFingerprint: Fingerprint(actualAnchors),
          MissingSamples: missing
            .Take(AnchorSampleLimit)
            .Select(FormatAnchor)
            .ToArray(),
          UnexpectedSamples: unexpected
            .Take(AnchorSampleLimit)
            .Select(FormatAnchor)
            .ToArray());
    }

    // 锚点排序：7 键严格全序，键与优先级与历史实现逐一相同（顺序即指纹，不得改动）。
    //
    // ⚠️ 不要把 LINQ 换成 Array.Sort / Span.Sort / 基数排序。2026-09-25 在真实语料
    // （NPC.cs，锚点 1,599,506）上做过同进程、轮转交错、5 次重复的对照，LINQ 每次都是最快的：
    //   LINQ OrderBy/ThenBy ×7      中位   807 ms（5 次：720 / 781 / 807 / 837 / 844）
    //   Array.Sort + Comparison     中位 1,368 ms
    //   Span.Sort + struct 比较器   中位 1,480 ms
    //   LSD 基数排序（28 趟 8 位）  中位 2,983 ms
    // 合成数据上的结论**相反**（LINQ 1,744 / Array.Sort 474），所以这类结论只能在真实语料上取证。
    // 推测原因（未单独取证）：LINQ 先一次性抽取键再比较，而自定义比较器每次按值传 28 字节结构体，
    // 随机访问下缓存不友好。
    private static IReadOnlyList<StableNodeAnchor> OrderAnchors(IEnumerable<StableNodeAnchor> anchors)
    {
        return anchors
          .OrderBy(anchor => anchor.Kind)
          .ThenBy(anchor => anchor.FilePathId)
          .ThenBy(anchor => anchor.SpanStart)
          .ThenBy(anchor => anchor.SpanEnd)
          .ThenBy(anchor => anchor.Role)
          .ThenBy(anchor => anchor.Ordinal)
          .ThenBy(anchor => anchor.ExtraKeyId)
          .ToArray();
    }

    // 锚点指纹：把排序后的 7 个定长字段逐字节写入 SHA-256。
    //
    // 等价性论证：SHA-256 是流式哈希，`SHA256(x ∥ y) == SHA256(x)` 后接 `y`。
    // 本方法只改变"追加到哈希的切分方式"（每字段一次 AppendData → 攒满缓冲一次），
    // 写入 hash 的**字节序列与顺序逐一不变**，故 AnchorFingerprint 逐字节相同。
    //
    // 实测（NPC.cs，锚点 1,599,506）：AppendData 11,196,542 次 → 685 次，
    // 追加段墙钟 901 → 213 ms（4.23×），Fingerprint 合计 ≈1,609 → 1,202 ms；
    // 语料 5 次运行 AnchorFingerprint 与 GraphSnapshotVersion 全部逐字节相同。
    //
    // 缓冲按"放不下就刷新"处理，不依赖容量对齐（AnchorByteCount=28 不整除 64 KiB；
    // 强行对齐会让末次写入跨缓冲，反而多一次 AppendData）。
    private static string Fingerprint(IEnumerable<StableNodeAnchor> anchors)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(FingerprintBufferCapacity);
        try
        {
            var position = 0;
            foreach (var anchor in OrderAnchors(anchors))
            {
                if (position + AnchorByteCount > buffer.Length)
                {
                    hash.AppendData(buffer.AsSpan(0, position));
                    position = 0;
                }

                var span = buffer.AsSpan(position);
                BinaryPrimitives.WriteInt32LittleEndian(span, (int)anchor.Kind);
                BinaryPrimitives.WriteUInt32LittleEndian(span[4..], anchor.FilePathId);
                BinaryPrimitives.WriteInt32LittleEndian(span[8..], anchor.SpanStart);
                BinaryPrimitives.WriteInt32LittleEndian(span[12..], anchor.SpanEnd);
                BinaryPrimitives.WriteInt32LittleEndian(span[16..], (int)anchor.Role);
                BinaryPrimitives.WriteInt32LittleEndian(span[20..], anchor.Ordinal);
                BinaryPrimitives.WriteUInt32LittleEndian(span[24..], anchor.ExtraKeyId);
                position += AnchorByteCount;
            }

            if (position > 0)
            {
                hash.AppendData(buffer.AsSpan(0, position));
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string FormatAnchor(StableNodeAnchor anchor)
    {
        return $"{anchor.Kind}:{anchor.FilePathId}:{anchor.SpanStart}:{anchor.SpanEnd}:" +
          $"{anchor.Role}:{anchor.Ordinal}:{anchor.ExtraKeyId}";
    }
}

public sealed record CpgBuildInventoryMetrics(
  int SyntaxFactCount,
  int OperationFactCount,
  int CfgFactCount,
  int TypedSymbolFactCount,
  int AnchorCount,
  string AnchorFingerprint,
  CpgBuildAnchorDiffMetrics? PreallocatedAnchorDiff = null);

public sealed record CpgBuildAnchorDiffMetrics(
  int ExpectedAnchorCount,
  int ActualAnchorCount,
  int MissingAnchorCount,
  int UnexpectedAnchorCount,
  string ExpectedFingerprint,
  string ActualFingerprint,
  IReadOnlyList<string> MissingSamples,
  IReadOnlyList<string> UnexpectedSamples)
{
    public bool IsEquivalent => MissingAnchorCount == 0 && UnexpectedAnchorCount == 0;
}
