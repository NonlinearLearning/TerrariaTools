namespace NLCPG.Builder.Concurrency;

public sealed class CpgWorkBatchBuilder
{
    private const int DefaultMaxMethodsPerBatch = 64;
    private const int DefaultMaxEstimatedBytesPerBatch = 1024 * 1024;
    private const int EstimatedBytesPerCostUnit = 64;
    private readonly CpgWorkBatchCostOptions _costOptions;
    private readonly int _maxMethodsPerBatch;
    private readonly int _maxEstimatedBytesPerBatch;

    public CpgWorkBatchBuilder(
      CpgWorkBatchCostOptions costOptions,
      int maxMethodsPerBatch = DefaultMaxMethodsPerBatch,
      int maxEstimatedBytesPerBatch = DefaultMaxEstimatedBytesPerBatch)
    {
        ArgumentNullException.ThrowIfNull(costOptions);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMethodsPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEstimatedBytesPerBatch, 1);
        _costOptions = costOptions;
        _maxMethodsPerBatch = maxMethodsPerBatch;
        _maxEstimatedBytesPerBatch = maxEstimatedBytesPerBatch;
    }

    public IReadOnlyList<CpgWorkBatch> Build(
      string sourceFilePath,
      IEnumerable<CpgWorkItem> workItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentNullException.ThrowIfNull(workItems);

        var orderedItems = NormalizeItems(sourceFilePath, workItems);
        var batches = new List<CpgWorkBatch>();
        var nextBatchId = 0L;

        AppendPreludeBatches(
          orderedItems.Where(item => item.Kind != CpgWorkItemKind.Method).ToArray(),
          batches,
          ref nextBatchId);
        AppendMethodBatches(
          orderedItems.Where(item => item.Kind == CpgWorkItemKind.Method).ToArray(),
          batches,
          ref nextBatchId);

        return batches.AsReadOnly();
    }

    private CpgWorkItem[] NormalizeItems(
      string sourceFilePath,
      IEnumerable<CpgWorkItem> workItems)
    {
        // sourceFilePath 现在只作标签/API 兼容保留（调用点仍传入），
        // 去重与排序一律改用每个 item 自带的 SourceFilePath。
        _ = sourceFilePath;
        // 跨文件装箱（D1）：允许多个文件的 work item 进入同一次 build。
        //
        // **主键仍是文件路径**，第二键是 ShardOrder（S5-2）：
        //   · 路径为主键是既有契约——StableOrder 是文件内局部序号，跨文件会重复，
        //     单凭它无法给出确定全序；且路径主键使**输入顺序无关**（见
        //     CpgWorkBatchCrossFilePackingTests.Build_WhenMultiFileInputOrderReversed…）。
        //   · ShardOrder 为第二键让**分片计划真正生效**：分片是**按文件**划分的
        //     （CpgWorkShardAssignment 以路径为键），故在文件内部按 ShardOrder 排列即
        //     「片 0 的方法 → 片 1 的方法 → …」连续成块 ⇒ 大文件被拆成若干标准大小的组后，
        //     装箱器**在片内**装箱，而不是无视计划按方法原序贪心。
        //     若无此键，计划只是改了序号，批次组成完全不受影响（等于没接上）。
        //   · 无计划的多文档走回退带（各文件区间互不重叠）；单文档时 ShardOrder == StableOrder
        //     ⇒ 退化为原「路径 → StableOrder」序，既有行为逐字不变。
        // 后续键保留为**确定性 tie-break**（去重前可能存在 ShardOrder 相同的重复项）。
        var ordered = workItems
          .OrderBy(item => item.SourceFilePath, StringComparer.Ordinal)
          .ThenBy(item => item.ShardOrder)
          .ThenBy(item => item.StableOrder)
          .ThenBy(item => item.SpanStart)
          .ThenBy(item => item.SpanEnd)
          .ThenBy(item => item.Kind)
          .ThenBy(item => item.MethodSymbolKey, StringComparer.Ordinal)
          .ThenBy(item => item.StableSpanIdentity, StringComparer.Ordinal)
          .ToArray();

        var seenSpans = new HashSet<string>(StringComparer.Ordinal);
        var seenMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<CpgWorkItem>(ordered.Length);
        foreach (var item in ordered)
        {
            if (!seenSpans.Add(item.StableSpanIdentity))
            {
                continue;
            }

            if (item.Kind == CpgWorkItemKind.Method &&
                item.MethodSymbolKey is not null &&
                !seenMethodKeys.Add(StableMethodIdentity(item)))
            {
                continue;
            }

            unique.Add(item);
        }

        return unique.ToArray();
    }

    /// <summary>
    /// 方法去重键。**必须**与 <see cref="CpgWorkItem.StableSpanIdentity"/> 一样带上文件限定前缀：
    /// <see cref="CpgWorkItem.MethodSymbolKey"/> 只是**文件内**唯一的符号键，
    /// 两个文件各自声明同名方法时键会相同；跨文件装箱若仍用裸键去重，
    /// 后出现的那一个会被静默丢弃（真实缺陷，非理论风险）。
    /// </summary>
    private static string StableMethodIdentity(CpgWorkItem item)
    {
        return $"{item.SourceFilePath}\u001f{item.MethodSymbolKey}";
    }

    private void AppendPreludeBatches(
      IReadOnlyList<CpgWorkItem> items,
      ICollection<CpgWorkBatch> batches,
      ref long nextBatchId)
    {
        AppendPackedBatches(items, CpgWorkBatchKind.Prelude, batches, ref nextBatchId);
    }

    private void AppendMethodBatches(
      IReadOnlyList<CpgWorkItem> items,
      ICollection<CpgWorkBatch> batches,
      ref long nextBatchId)
    {
        var current = new List<CpgWorkItem>();
        var currentCost = 0;
        var currentBytes = 0;
        foreach (var item in items)
        {
            // **装箱策略**（非尺寸分类）：cost 严格大于隔离阈值的方法独占一批。
            // 历史上此处用 Classify(...) 的 Large/Oversized 判定，而 Large 下界 =
            // MediumMaxCost ⇒ 隔离阈值被硬绑在 200。现改为读 IsolationCostThreshold，
            // 其默认值仍等于 MediumMaxCost ⇒ 既有行为逐字不变；
            // 放宽它（见 CpgWorkBatchCostOptions.Packing）即可让 cost ∈ (200, 1500]
            // 的方法配对装箱。
            if (item.EstimatedCost > _costOptions.IsolationCostThreshold)
            {
                Flush(current, CpgWorkBatchKind.Methods, batches, ref nextBatchId);
                currentCost = 0;
                currentBytes = 0;
                AddBatch(new[] { item }, CpgWorkBatchKind.Methods, batches, ref nextBatchId);
                continue;
            }

            if (current.Count > 0 &&
                (currentCost + item.EstimatedCost > _costOptions.MaxBatchCost ||
                 currentCost + item.EstimatedCost > _costOptions.TargetBatchCost ||
                 current.Count >= _maxMethodsPerBatch ||
                 currentBytes + EstimateBytes(item) > _maxEstimatedBytesPerBatch))
            {
                Flush(current, CpgWorkBatchKind.Methods, batches, ref nextBatchId);
                currentCost = 0;
                currentBytes = 0;
            }

            current.Add(item);
            currentCost += item.EstimatedCost;
            currentBytes += EstimateBytes(item);
            if (currentCost >= _costOptions.TargetBatchCost ||
                currentBytes >= _maxEstimatedBytesPerBatch ||
                current.Count >= _maxMethodsPerBatch)
            {
                Flush(current, CpgWorkBatchKind.Methods, batches, ref nextBatchId);
                currentCost = 0;
                currentBytes = 0;
            }
        }

        Flush(current, CpgWorkBatchKind.Methods, batches, ref nextBatchId);
    }

    private void AppendPackedBatches(
      IReadOnlyList<CpgWorkItem> items,
      CpgWorkBatchKind kind,
      ICollection<CpgWorkBatch> batches,
      ref long nextBatchId)
    {
        var current = new List<CpgWorkItem>();
        var currentCost = 0;
        var currentBytes = 0;
        foreach (var item in items)
        {
            var itemBytes = EstimateBytes(item);
            if (current.Count > 0 &&
                (currentCost + item.EstimatedCost > _costOptions.MaxBatchCost ||
                 currentCost + item.EstimatedCost > _costOptions.TargetBatchCost ||
                 current.Count >= _maxMethodsPerBatch ||
                 currentBytes + itemBytes > _maxEstimatedBytesPerBatch))
            {
                Flush(current, kind, batches, ref nextBatchId);
                currentCost = 0;
                currentBytes = 0;
            }

            current.Add(item);
            currentCost += item.EstimatedCost;
            currentBytes += itemBytes;
            if (currentCost >= _costOptions.TargetBatchCost ||
                currentBytes >= _maxEstimatedBytesPerBatch ||
                current.Count >= _maxMethodsPerBatch)
            {
                Flush(current, kind, batches, ref nextBatchId);
                currentCost = 0;
                currentBytes = 0;
            }
        }

        Flush(current, kind, batches, ref nextBatchId);
    }

    private void Flush(
      List<CpgWorkItem> current,
      CpgWorkBatchKind kind,
      ICollection<CpgWorkBatch> batches,
      ref long nextBatchId)
    {
        if (current.Count == 0)
        {
            return;
        }

        AddBatch(current, kind, batches, ref nextBatchId);
        current.Clear();
    }

    private static void AddBatch(
      IReadOnlyList<CpgWorkItem> items,
      CpgWorkBatchKind kind,
      ICollection<CpgWorkBatch> batches,
      ref long nextBatchId)
    {
        var estimatedCost = items.Sum(item => item.EstimatedCost);
        var estimatedBytes = items.Sum(EstimateBytes);
        // ShardOrder 取批内**最小**值：NormalizeItems 已按（文件路径, StableOrder）排序，
        // 单文件时文件路径恒定 ⇒ 退化为按 StableOrder 排序，且 items[0].ShardOrder == items[0].StableOrder，
        // 故此处行为对单文件输入逐字不变；跨文件装箱时它给出该批在全局调度序上的下界。
        var shardOrder = items.Min(item => item.ShardOrder);
        // items[0].SourceFilePath 仅作**标签**：跨文件装箱时它只是批内首个 item 的文件，
        // 并**不是**该批的路由键。任何按批路由/归属的判断都必须改用每个 item 的
        // SourceFilePath（T4 负责接线）；此处不改签名以免破坏既有调用点。
        batches.Add(new CpgWorkBatch(
          nextBatchId,
          items[0].SourceFilePath,
          items[0].StableOrder,
          items,
          estimatedCost,
          estimatedNodeCount: items.Count,
          estimatedBytes,
          kind,
          shardOrder));
        nextBatchId += 1;
    }

    private static int EstimateBytes(CpgWorkItem item)
    {
        return checked(item.EstimatedCost * EstimatedBytesPerCostUnit);
    }
}
