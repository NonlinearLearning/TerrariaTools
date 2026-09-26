namespace NLISSN.Application;

/// <summary>
/// 文档分片计划器：把一个文件的**完整函数**集合切成 K 个标准大小的分片（能力 D2）。
/// <para>
/// <b>职责边界</b>：本类是**纯计算**，不接收 Roslyn <c>SemanticModel</c>，也不读文件；
/// 方法根发现（`GetOperationRootPlans` 需要 <c>SemanticModel</c>，位于目录层）由调用方
/// 完成后，把结果转成 <see cref="DocumentMethodDescriptor"/> 传入。
/// </para>
/// <para>
/// ⚠ <b>最重要的规则：原子单位是完整函数，绝不在函数体内部下刀。</b>
/// 若某个方法自身的成本就已经超过目标片行数，它**独占一片**、
/// <b>允许超出目标</b>且永不被切分。因此本计划器的承诺只能是
/// 「在保持函数完整的前提下尽量贴近目标」，**不是**「消除所有超标项」——
/// 后者由构造方式决定不可达（设计文档 §5.1 硬约束 1，:423-428）。
/// </para>
/// <para>
/// 算法：按「方法体总行数」分类文件；非大文件直接单片（K=1）；
/// 大文件按成本降序（LPT 序）做确定性贪心装箱——每个方法放进「当前装载量最小、
/// 且放进去仍不超目标」的片，放不下则新开一片。
/// </para>
/// </summary>
public static class DocumentShardPlanner
{
    /// <summary>
    /// 每个成本单位折算的估算字节数。
    /// <para>
    /// 与批组装层**逐字一致**：<c>CpgWorkBatchBuilder.EstimatedBytesPerCostUnit = 64</c>
    /// （src\NLCPG\Builder\Concurrency\CpgWorkBatchBuilder.cs:7，使用点 :241
    /// 的 <c>checked(item.EstimatedCost * EstimatedBytesPerCostUnit)</c>）。
    /// 两处必须同源，否则计划层与执行层对「字节额度」的理解会分歧。
    /// </para>
    /// </summary>
    public const int EstimatedBytesPerCostUnit = 64;

    /// <summary>
    /// 跨文件 <see cref="DocumentShard.StableOrder"/> 的步长。
    /// <para>
    /// 取 <c>int.MaxValue</c>，使全局序号的上界按「文件序号 × 2^31-1 + 片内序号」增长：
    /// 只要文件数不超过约 4.29e9 个、单文件片数不超过 2^31-1 个，
    /// 该编码就**保持单调且唯一**（溢出由 <c>checked</c> 显式抛出，不会静默回绕）。
    /// 这与 <c>CpgWorkItem.ShardOrder</c> 把「文件内局部序号」与「跨文件全局序号」
    /// 分开承担的思路一致（src\NLCPG\Builder\Concurrency\CpgWorkItem.cs:71-77）。
    /// </para>
    /// </summary>
    public const long FileOrdinalStride = int.MaxValue;

    /// <summary>
    /// 为单个文件生成分片计划。
    /// <para>
    /// <b>完整函数规则</b>：分片边界只落在方法之间；成本超过
    /// <see cref="DocumentShardPlannerOptions.LargeFileShardTargetLines"/> 的单个方法
    /// 独占一片并允许超标，绝不切开。
    /// </para>
    /// </summary>
    /// <param name="filePath">文件路径；用于计划的标签与跨文件身份。</param>
    /// <param name="methods">已抽取的方法描述符；允许乱序，内部会按方法序号归一化。</param>
    /// <param name="options">
    /// 阈值配置；为 <c>null</c> 时用 <see cref="DocumentShardPlannerOptions.Default"/>。
    /// </param>
    /// <param name="fileOrdinal">
    /// 该文件在**全局文件序**中的序号（从 0 起），用于导出跨文件唯一的
    /// <see cref="DocumentShard.StableOrder"/>。多文件场景请用 <see cref="PlanAll"/> 或
    /// <see cref="AssignFileOrdinals"/> 取得与 <see cref="PlanAll"/> 一致的序号。
    /// </param>
    /// <returns>该文件的确定性分片计划。</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="methods"/> 或其中元素为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fileOrdinal"/> 为负；或某个描述符的
    /// <see cref="DocumentMethodDescriptor.MethodOrder"/> 为负、成本小于 1、方法序号重复。
    /// </exception>
    public static DocumentShardPlan Plan(
      string filePath,
      IReadOnlyList<DocumentMethodDescriptor> methods,
      DocumentShardPlannerOptions? options = null,
      int fileOrdinal = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentOutOfRangeException.ThrowIfNegative(fileOrdinal);

        var plannerOptions = options ?? DocumentShardPlannerOptions.Default;
        var orderedMethods = NormalizeMethods(methods);
        var totalCost = SumCost(orderedMethods);
        var sizeClass = Classify(totalCost, plannerOptions);
        var shards = sizeClass == FileSizeClass.Large
          ? PackLargeFile(orderedMethods, plannerOptions, fileOrdinal)
          : new List<DocumentShard> { CreateShard(orderedMethods, 0, fileOrdinal) };

        return new DocumentShardPlan(
          filePath,
          sizeClass,
          shards.AsReadOnly(),
          shards.Sum(shard => shard.EstimatedBytes));
    }

    /// <summary>
    /// 为一批文件生成计划，并保证 <see cref="DocumentShard.StableOrder"/> 跨文件**唯一且单调**。
    /// <para>
    /// 文件序号由 <see cref="AssignFileOrdinals"/> 按文件路径的**序数序**分配
    /// （字符串比较用 <see cref="StringComparer.Ordinal"/>），故与输入顺序无关。
    /// </para>
    /// </summary>
    /// <param name="files">输入文件集合；集合顺序不影响每个文件自身的计划内容。</param>
    /// <param name="options">阈值配置；为 <c>null</c> 时用默认值。</param>
    /// <returns>**与输入同序**的计划列表（第 i 项对应 <paramref name="files"/>[i]）。</returns>
    public static IReadOnlyList<DocumentShardPlan> PlanAll(
      IReadOnlyList<DocumentShardInputFile> files,
      DocumentShardPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var ordinals = AssignFileOrdinals(files);
        var plans = new List<DocumentShardPlan>(files.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            ArgumentNullException.ThrowIfNull(file);
            plans.Add(Plan(file.FilePath, file.Methods, options, ordinals[file.FilePath]));
        }

        return plans.AsReadOnly();
    }

    /// <summary>
    /// 按文件路径的序数序，为每个输入文件分配**跨文件全局文件序号**（0 起连续）。
    /// <para>
    /// 排序键只有「路径（<see cref="StringComparer.Ordinal"/>）→ 原始下标」，
    /// <b>不</b>依赖 <c>Dictionary</c> 枚举序或哈希序，故同一输入必然给出同一映射。
    /// </para>
    /// </summary>
    /// <param name="files">输入文件集合；每个路径最多出现一次。</param>
    /// <returns>文件路径 → 文件序号的只读映射，路径键用序数比较。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> 或其中元素为 null。</exception>
    /// <exception cref="ArgumentException">
    /// 某个 <see cref="DocumentShardInputFile.FilePath"/> 为 null/空白，
    /// 或同一路径重复出现（重复路径无法得到唯一的跨文件序号，故显式拒绝而非静默取值）。
    /// </exception>
    public static IReadOnlyDictionary<string, int> AssignFileOrdinals(
      IReadOnlyList<DocumentShardInputFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var indexed = new List<(string FilePath, int OriginalIndex)>(files.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            ArgumentNullException.ThrowIfNull(file);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.FilePath);
            indexed.Add((file.FilePath, index));
        }

        // 排序键只有「路径（序数）→ 原始下标」；原始下标仅用于打破路径并列，
        // 而路径并列本身随后会被显式拒绝。
        var ordered = indexed
          .OrderBy(entry => entry.FilePath, StringComparer.Ordinal)
          .ThenBy(entry => entry.OriginalIndex)
          .ToList();

        var ordinals = new Dictionary<string, int>(files.Count, StringComparer.Ordinal);
        for (var index = 0; index < ordered.Count; index++)
        {
            if (!ordinals.TryAdd(ordered[index].FilePath, index))
            {
                throw new ArgumentException(
                  "同一文件路径不得在输入中重复出现，否则无法给出唯一的跨文件序号。",
                  nameof(files));
            }
        }

        return ordinals;
    }

    /// <summary>
    /// 按方法体总行数把文件分档；`≤ SmallFileMaxLines` 为小、`≤ MediumFileMaxLines` 为中，其余为大。
    /// </summary>
    /// <param name="totalCost">文件的方法体总行数。</param>
    /// <param name="options">阈值配置。</param>
    /// <returns>文件大小档。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalCost"/> 为负。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null。</exception>
    public static FileSizeClass Classify(int totalCost, DocumentShardPlannerOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCost);
        ArgumentNullException.ThrowIfNull(options);

        if (totalCost <= options.SmallFileMaxLines)
        {
            return FileSizeClass.Small;
        }

        return totalCost <= options.MediumFileMaxLines
          ? FileSizeClass.Medium
          : FileSizeClass.Large;
    }

    /// <summary>
    /// 归一化方法描述符：校验成本与序号，按方法序号升序排列（方法序号是计划输出与去重的主键）。
    /// </summary>
    private static List<DocumentMethodDescriptor> NormalizeMethods(
      IReadOnlyList<DocumentMethodDescriptor> methods)
    {
        var seenOrders = new HashSet<int>();
        var ordered = new List<DocumentMethodDescriptor>(methods.Count);
        foreach (var method in methods)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentOutOfRangeException.ThrowIfNegative(method.MethodOrder);
            ArgumentOutOfRangeException.ThrowIfLessThan(method.EstimatedCost, 1);
            if (!seenOrders.Add(method.MethodOrder))
            {
                // 同一方法出现两次会让「每个方法恰好属于一个分片」的守恒不成立，
                // 且会让 StableOrder 与载荷下标错位，必须在入口拒绝而不是静默去重。
                throw new ArgumentOutOfRangeException(
                  nameof(methods),
                  method.MethodOrder,
                  "方法序号必须文件内唯一。");
            }

            ordered.Add(method);
        }

        ordered.Sort(static (left, right) => left.MethodOrder.CompareTo(right.MethodOrder));
        return ordered;
    }

    /// <summary>汇总方法体总行数；用 <c>long</c> 中间量避免大文件在求和中溢出。</summary>
    private static int SumCost(IReadOnlyList<DocumentMethodDescriptor> methods)
    {
        var total = 0L;
        foreach (var method in methods)
        {
            total += method.EstimatedCost;
        }

        return checked((int)total);
    }

    /// <summary>
    /// 大文件的确定性贪心装箱：先按成本降序（LPT），逐一放入当前**剩余容量最大**的分片；
    /// 单个方法自身超过目标容量时独占新片，且不参与后续装填。
    /// </summary>
    /// <remarks>
    /// 全程只使用「成本 → 方法序号」这一显式全序作为排序键，不使用字典枚举序或哈希序，
    /// 故同一输入必然给出逐字相同的输出。
    /// </remarks>
    private static List<DocumentShard> PackLargeFile(
      List<DocumentMethodDescriptor> orderedMethods,
      DocumentShardPlannerOptions options,
      int fileOrdinal)
    {
        var target = options.LargeFileShardTargetLines;

        // 主序：成本降序（最大优先，LPT）；并列时按方法序号升序，保证全序且确定。
        var byCost = new List<DocumentMethodDescriptor>(orderedMethods);
        byCost.Sort(static (left, right) =>
        {
            var byEstimatedCost = right.EstimatedCost.CompareTo(left.EstimatedCost);
            return byEstimatedCost != 0
              ? byEstimatedCost
              : left.MethodOrder.CompareTo(right.MethodOrder);
        });

        // 超标方法：独占一片，永不切分（完整函数规则的直接后果）。
        var packs = new List<List<DocumentMethodDescriptor>>();
        foreach (var method in byCost)
        {
            if (method.EstimatedCost > target)
            {
                packs.Add(new List<DocumentMethodDescriptor> { method });
            }
        }

        // 其余方法：贪心装入「装完后仍不超目标、且当前成本最大」的片；装不下则新开一片。
        // 此处的容量判断是**软目标**：它只影响装箱结果，不构成「不得超标」的承诺。
        var residual = new List<DocumentMethodDescriptor>();
        foreach (var method in byCost)
        {
            if (method.EstimatedCost <= target)
            {
                residual.Add(method);
            }
        }

        var packCosts = new List<int>(packs.Count);
        foreach (var pack in packs)
        {
            packCosts.Add(pack[0].EstimatedCost);
        }

        foreach (var method in residual)
        {
            var chosen = -1;
            for (var index = 0; index < packs.Count; index++)
            {
                if (packCosts[index] + method.EstimatedCost > target)
                {
                    continue;
                }

                // 「剩余容量最大」等价于「当前成本最小」；并列时取更小下标 ⇒ 确定。
                if (chosen < 0 || packCosts[index] < packCosts[chosen])
                {
                    chosen = index;
                }
            }

            if (chosen < 0)
            {
                packs.Add(new List<DocumentMethodDescriptor> { method });
                packCosts.Add(method.EstimatedCost);
                continue;
            }

            packs[chosen].Add(method);
            packCosts[chosen] += method.EstimatedCost;
        }

        // 片内按方法序号升序输出；片与片之间按「首方法序号」升序输出（首方法即该片最小方法序号），
        // 使 ShardIndex 与 StableOrder 都只依赖输入的确定性全序。
        var normalizedPacks =
          new List<(int LeadMethodOrder, List<DocumentMethodDescriptor> Methods)>(packs.Count);
        foreach (var pack in packs)
        {
            pack.Sort(static (left, right) => left.MethodOrder.CompareTo(right.MethodOrder));
            normalizedPacks.Add((pack[0].MethodOrder, pack));
        }

        normalizedPacks.Sort(static (left, right) =>
          left.LeadMethodOrder.CompareTo(right.LeadMethodOrder));

        var shards = new List<DocumentShard>(normalizedPacks.Count);
        for (var index = 0; index < normalizedPacks.Count; index++)
        {
            shards.Add(CreateShard(normalizedPacks[index].Methods, index, fileOrdinal));
        }

        return shards;
    }

    /// <summary>由方法集合构造分片，并导出成本、字节数与被调用方赋予的全局稳定序号。</summary>
    private static DocumentShard CreateShard(
      List<DocumentMethodDescriptor> methods,
      int shardIndex,
      int fileOrdinal)
    {
        var methodOrders = new List<int>(methods.Count);
        var cost = 0;
        foreach (var method in methods)
        {
            methodOrders.Add(method.MethodOrder);
            cost += method.EstimatedCost;
        }

        return new DocumentShard(
          StableOrder: ComputeStableOrder(fileOrdinal, shardIndex),
          ShardIndex: shardIndex,
          MethodOrders: methodOrders.AsReadOnly(),
          EstimatedCost: cost,
          EstimatedBytes: checked((long)cost * EstimatedBytesPerCostUnit));
    }

    /// <summary>
    /// 全局单调、跨文件唯一的稳定序号编码：<c>文件序号 × FileOrdinalStride + 片内序号</c>。
    /// <para>溢出用 <c>checked</c> 显式抛出，绝不静默回绕（回绕会破坏单调性）。</para>
    /// </summary>
    private static long ComputeStableOrder(int fileOrdinal, int shardIndex)
    {
        return checked((fileOrdinal * FileOrdinalStride) + shardIndex);
    }
}
