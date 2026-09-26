namespace NLCPG.Builder.Concurrency;

/// <summary>
/// 携带**来源文件**的 worker 结果，使归并回调能把事实路由到该文件的图（D1 按项路由）。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：T3 放宽了装箱的单文件断言后，一个批次可以**跨多个文件**。
/// 此时「一个批次 → 一个结果 → 写入 <c>context.Graph</c>」的旧框架会把 B 文件的事实
/// 写进 A 文件的图（设计 R10 定性为「静默错误，非崩溃」）。
/// </para>
/// <para>
/// 故 worker 必须按 <see cref="CpgWorkItem.SourceFilePath"/> **把批次拆成每文件一个结果**，
/// 归并回调再用 <see cref="SourceRoutedGroup{T}.SourceFilePath"/> 解析目标图。
/// 这样**一个批次仍然只产出一个结果对象**（执行器的归并/度量契约不必改动），
/// 但结果内部已按文件分离。单文件批次下恒只有一个分组，行为与改造前逐字一致。
/// </para>
/// </remarks>
internal sealed record SourceRoutedGroup<T>(string SourceFilePath, IReadOnlyList<T> Items);

/// <summary>
/// 按源文件把批次条目分组，并保持条目在组内的原有次序。
/// </summary>
/// <remarks>
/// <para>
/// 分组**次序**必须是确定性的：先按条目在批次中的出现序确定「首次出现的文件」次序，
/// 再按该次序产出分组。刻意**不**用 <c>GroupBy</c>（其产出的组序在实现上依赖遍历顺序，
/// 虽当前也稳定，但把「稳定」写成显式的两遍扫描更可证伪）。
/// </para>
/// <para>
/// 单文件批次（改造前的**全部**批次）恒返回一个分组，故调用方在单文件路径上的行为不变。
/// </para>
/// </remarks>
internal static class SourceFilePartition
{
    /// <summary>
    /// 把批次条目按 <see cref="CpgWorkItem.SourceFilePath"/> 分组；
    /// 组内保持 <paramref name="items"/> 的传入次序，组间按文件首次出现序。
    /// </summary>
    internal static IReadOnlyList<SourceRoutedGroup<CpgWorkItem>> BySourceFile(
      IReadOnlyList<CpgWorkItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // 第一遍：确定文件的首次出现次序（确定性）。
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (seen.Add(item.SourceFilePath))
            {
                order.Add(item.SourceFilePath);
            }
        }

        // 第二遍：按该次序装桶，组内保序。
        var buckets = new Dictionary<string, List<CpgWorkItem>>(StringComparer.Ordinal);
        foreach (var filePath in order)
        {
            buckets[filePath] = new List<CpgWorkItem>();
        }

        foreach (var item in items)
        {
            buckets[item.SourceFilePath].Add(item);
        }

        var groups = new List<SourceRoutedGroup<CpgWorkItem>>(order.Count);
        foreach (var filePath in order)
        {
            groups.Add(new SourceRoutedGroup<CpgWorkItem>(filePath, buckets[filePath]));
        }

        return groups;
    }
}
