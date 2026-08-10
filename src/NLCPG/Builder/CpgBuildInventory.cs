using System.Buffers.Binary;
using System.Security.Cryptography;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder;

// 汇总一次构图已产生的只读事实计数，作为预分配和算法实验的共同审计基线。
internal sealed class CpgBuildInventory
{
    private const int AnchorSampleLimit = 8;

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

    private static string Fingerprint(IEnumerable<StableNodeAnchor> anchors)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var anchor in OrderAnchors(anchors))
        {
            AppendInt32(hash, (int)anchor.Kind);
            AppendUInt32(hash, anchor.FilePathId);
            AppendInt32(hash, anchor.SpanStart);
            AppendInt32(hash, anchor.SpanEnd);
            AppendInt32(hash, (int)anchor.Role);
            AppendInt32(hash, anchor.Ordinal);
            AppendUInt32(hash, anchor.ExtraKeyId);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string FormatAnchor(StableNodeAnchor anchor)
    {
        return $"{anchor.Kind}:{anchor.FilePathId}:{anchor.SpanStart}:{anchor.SpanEnd}:" +
          $"{anchor.Role}:{anchor.Ordinal}:{anchor.ExtraKeyId}";
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
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
