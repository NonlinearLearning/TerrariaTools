using NLCPG.Builder.Streaming;
using NLCPG.Model;

namespace NLCPG.Builder.Concurrency;

public sealed record LocalCpgFragment
{
    public LocalCpgFragment(
      long batchId,
      string sourceFilePath,
      int stableOrder,
      IReadOnlyList<CpgNodeDescriptor> nodes,
      IReadOnlyList<CpgEdgeCandidate> edges,
      IReadOnlyList<CpgMethodSummary> methodSummaries,
      IReadOnlyList<CpgBoundaryReference> boundaryReferences,
      CpgFragmentMetrics metrics,
      IReadOnlyList<CpgDiagnostic> diagnostics)
      : this(
        batchId,
        sourceFilePath,
        stableOrder,
        nodes,
        edges,
        methodSummaries,
        boundaryReferences,
        metrics,
        diagnostics,
        ownsCollections: false)
    {
    }

    // 复制入口与接管入口共用同一份参数与边界校验，保留原有异常类型、参数名和校验顺序；
    // 只有 ownsCollections 决定五个集合是否还要再复制一次。
    //
    // ownsCollections 只由 CreateOwned 的数组形参传入，绝不通过运行时类型判断
    // （如 nodes is CpgNodeDescriptor[]）从公开入口接管调用方数组。
    private LocalCpgFragment(
      long batchId,
      string sourceFilePath,
      int stableOrder,
      IReadOnlyList<CpgNodeDescriptor> nodes,
      IReadOnlyList<CpgEdgeCandidate> edges,
      IReadOnlyList<CpgMethodSummary> methodSummaries,
      IReadOnlyList<CpgBoundaryReference> boundaryReferences,
      CpgFragmentMetrics metrics,
      IReadOnlyList<CpgDiagnostic> diagnostics,
      bool ownsCollections)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentOutOfRangeException.ThrowIfNegative(stableOrder);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(methodSummaries);
        ArgumentNullException.ThrowIfNull(boundaryReferences);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // 校验全部完成后才赋值，故接管失败不会改动生产者数组。
        var availableAnchors = nodes
          .Select(node => node.Anchor)
          .Concat(boundaryReferences.Select(reference => reference.Anchor))
          .ToHashSet();
        foreach (var edge in edges)
        {
            if (!availableAnchors.Contains(edge.SourceAnchor) ||
                !availableAnchors.Contains(edge.TargetAnchor))
            {
                throw new ArgumentException(
                  "Fragment edges must reference a local node or an explicit boundary reference.",
                  nameof(edges));
            }
        }

        BatchId = batchId;
        SourceFilePath = sourceFilePath;
        StableOrder = stableOrder;
        Nodes = Array.AsReadOnly(
          ownsCollections ? (CpgNodeDescriptor[])nodes : nodes.ToArray());
        Edges = Array.AsReadOnly(
          ownsCollections ? (CpgEdgeCandidate[])edges : edges.ToArray());
        MethodSummaries = Array.AsReadOnly(
          ownsCollections ? (CpgMethodSummary[])methodSummaries : methodSummaries.ToArray());
        BoundaryReferences = Array.AsReadOnly(
          ownsCollections ? (CpgBoundaryReference[])boundaryReferences : boundaryReferences.ToArray());
        Metrics = metrics;
        Diagnostics = Array.AsReadOnly(
          ownsCollections ? (CpgDiagnostic[])diagnostics : diagnostics.ToArray());
    }

    /// <summary>
    /// 接管生产者独占的五个数组，跳过公开入口的防御性复制。
    /// </summary>
    /// <remarks>
    /// 成功返回后生产者放弃这些数组的写入与复用；fragment 只以只读包装暴露它们。
    /// 校验失败时不改动输入，所有权仍属于生产者。共享的 <see cref="Array.Empty{T}"/> 是零长度特例。
    /// 仅转移数组存储，不宣称深拷贝元素内部引用。
    /// </remarks>
    internal static LocalCpgFragment CreateOwned(
      long batchId,
      string sourceFilePath,
      int stableOrder,
      CpgNodeDescriptor[] nodes,
      CpgEdgeCandidate[] edges,
      CpgMethodSummary[] methodSummaries,
      CpgBoundaryReference[] boundaryReferences,
      CpgFragmentMetrics metrics,
      CpgDiagnostic[] diagnostics)
    {
        return new LocalCpgFragment(
          batchId,
          sourceFilePath,
          stableOrder,
          nodes,
          edges,
          methodSummaries,
          boundaryReferences,
          metrics,
          diagnostics,
          ownsCollections: true);
    }

    public long BatchId { get; }

    public string SourceFilePath { get; }

    public int StableOrder { get; }

    public IReadOnlyList<CpgNodeDescriptor> Nodes { get; }

    public IReadOnlyList<CpgEdgeCandidate> Edges { get; }

    public IReadOnlyList<CpgMethodSummary> MethodSummaries { get; }

    public IReadOnlyList<CpgBoundaryReference> BoundaryReferences { get; }

    public CpgFragmentMetrics Metrics { get; }

    public IReadOnlyList<CpgDiagnostic> Diagnostics { get; }
}
