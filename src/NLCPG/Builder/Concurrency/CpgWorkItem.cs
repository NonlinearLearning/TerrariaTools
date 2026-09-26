namespace NLCPG.Builder.Concurrency;

public enum CpgWorkItemKind
{
    FilePrelude,
    Declaration,
    Method,
}

public sealed record CpgWorkItem
{
    public CpgWorkItem(
      int stableOrder,
      string sourceFilePath,
      string? methodSymbolKey,
      int spanStart,
      int spanEnd,
      int estimatedCost,
      CpgWorkItemKind kind)
      : this(
        stableOrder,
        sourceFilePath,
        methodSymbolKey,
        spanStart,
        spanEnd,
        estimatedCost,
        kind,
        shardOrder: stableOrder)
    {
    }

    /// <summary>
    /// 供跨文件批次使用：<paramref name="stableOrder"/> 仍是**文件内局部序号**
    /// （载荷下标语义，见 <c>DataFlowPass</c> 的 <c>methodPartitions[item.StableOrder]</c>），
    /// <paramref name="shardOrder"/> 才是**跨文件全局单调**的调度序号。
    /// </summary>
    public CpgWorkItem(
      int stableOrder,
      string sourceFilePath,
      string? methodSymbolKey,
      int spanStart,
      int spanEnd,
      int estimatedCost,
      CpgWorkItemKind kind,
      int shardOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stableOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(shardOrder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentOutOfRangeException.ThrowIfNegative(spanStart);
        if (spanEnd < spanStart)
        {
            throw new ArgumentOutOfRangeException(nameof(spanEnd));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(estimatedCost, 1);
        StableOrder = stableOrder;
        ShardOrder = shardOrder;
        SourceFilePath = sourceFilePath;
        MethodSymbolKey = methodSymbolKey;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        EstimatedCost = estimatedCost;
        Kind = kind;
    }

    /// <summary>
    /// **文件内局部**稳定序号。同时充当载荷查找下标（`DataFlowPass`、`CallGraphPass`），
    /// 故其语义**不得**改为全局序号——跨文件全局单调序号见 <see cref="ShardOrder"/>。
    /// </summary>
    public int StableOrder { get; }

    /// <summary>
    /// **跨文件全局单调**调度序号。默认等于 <see cref="StableOrder"/>（单文件时二者一致），
    /// 跨文件装箱时由计划器赋予全局值。只有调度/归并使用它，载荷查找**一律**用 <see cref="StableOrder"/>。
    /// </summary>
    public int ShardOrder { get; }

    public string SourceFilePath { get; }

    public string? MethodSymbolKey { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int EstimatedCost { get; }

    public CpgWorkItemKind Kind { get; }

    public string StableSpanIdentity =>
      $"{SourceFilePath}\u001f{SpanStart}:{SpanEnd}";
}
