namespace NLCPG.Builder.Concurrency;

public enum CpgWorkBatchKind
{
    Prelude,
    Methods,
    Mixed,
}

public sealed record CpgWorkBatch
{
    public CpgWorkBatch(
      long batchId,
      string sourceFilePath,
      int stableOrder,
      IReadOnlyList<CpgWorkItem> items,
      int estimatedCost,
      int estimatedNodeCount,
      int estimatedBytes,
      CpgWorkBatchKind kind)
      : this(
        batchId,
        sourceFilePath,
        stableOrder,
        items,
        estimatedCost,
        estimatedNodeCount,
        estimatedBytes,
        kind,
        shardOrder: stableOrder)
    {
    }

    /// <summary>
    /// 供跨文件批次使用：<paramref name="stableOrder"/> 保留文件内局部语义，
    /// <paramref name="shardOrder"/> 承担跨文件全局单调调度序号。
    /// </summary>
    public CpgWorkBatch(
      long batchId,
      string sourceFilePath,
      int stableOrder,
      IReadOnlyList<CpgWorkItem> items,
      int estimatedCost,
      int estimatedNodeCount,
      int estimatedBytes,
      CpgWorkBatchKind kind,
      int shardOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentOutOfRangeException.ThrowIfNegative(stableOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(shardOrder);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(estimatedCost, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedNodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedBytes);

        BatchId = batchId;
        SourceFilePath = sourceFilePath;
        StableOrder = stableOrder;
        ShardOrder = shardOrder;
        Items = items.ToArray();
        EstimatedCost = estimatedCost;
        EstimatedNodeCount = estimatedNodeCount;
        EstimatedBytes = estimatedBytes;
        Kind = kind;
    }

    public long BatchId { get; }

    public string SourceFilePath { get; }

    /// <summary>
    /// **文件内局部**稳定序号（保留原有语义，不得改为全局序号）。
    /// </summary>
    public int StableOrder { get; }

    /// <summary>
    /// **跨文件全局单调**调度序号，是调度与归并的唯一排序依据。默认等于 <see cref="StableOrder"/>。
    /// </summary>
    public int ShardOrder { get; }

    public IReadOnlyList<CpgWorkItem> Items { get; }

    public int EstimatedCost { get; }

    public int EstimatedNodeCount { get; }

    public int EstimatedBytes { get; }

    public CpgWorkBatchKind Kind { get; }
}
