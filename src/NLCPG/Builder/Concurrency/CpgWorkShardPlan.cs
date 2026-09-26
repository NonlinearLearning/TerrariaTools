namespace NLCPG.Builder.Concurrency;

/// <summary>
/// 一个分片：覆盖若干**完整方法**（方法序号），不切开任何方法体。
/// </summary>
/// <param name="MethodOrders">
/// 该片负责的方法序号（**文件内局部**，与操作根 <c>order</c> 同源）。
/// 顺序不参与语义，分片内的全局次序由 <see cref="CpgWorkShardPlan"/> 按
/// 「分片序 → 片内列出的顺序」统一导出。
/// </param>
public sealed record CpgWorkShard(
  IReadOnlyList<int> MethodOrders);

/// <summary>单个源文件的分片集合（**有序**：分片序号即其在列表中的下标）。</summary>
/// <param name="SourceFilePath">该文件的路径；与 <see cref="CpgWorkItem.SourceFilePath"/> 同一书写形式。</param>
/// <param name="Shards">该文件的分片，顺序即分片序号。</param>
public sealed record CpgWorkShardAssignment(
  string SourceFilePath,
  IReadOnlyList<CpgWorkShard> Shards);

/// <summary>
/// **NLCPG 侧的中立分片契约**（设计 Task S5-2）。
/// <para>
/// <b>为什么需要这一层：</b>分片计划由**应用层**的 <c>DocumentShardPlanner</c> 拥有，
/// 依赖方向是 <c>Application → NLCPG</c>。故 NLCPG <b>不得</b>直接接收
/// <c>Application.DocumentShardPlan</c>，也不得新增 <c>NLCPG → Application</c> 引用
/// （<c>2026-09-24-unified-work-scheduler-execution.md:508</c>）。
/// 本类型只由 <see cref="int"/> / <see cref="string"/> 构成，正是那一层中立表达。
/// </para>
/// <para>
/// <b>⚠ 序号方案（本设计唯一容易做错的地方）：</b>
/// 调度要求全部 <see cref="CpgWorkItem.ShardOrder"/> <b>唯一</b>。原因是
/// <c>CpgWorkBatchBuilder.AddBatch</c> 把批序号取为批内 <c>Min(ShardOrder)</c>，
/// 而执行器校验批序号唯一（<c>CpgWorkBatchExecutor.cs:578</c> 抛
/// <c>"WorkBatch shard orders must be unique."</c>）。
/// </para>
/// <para>
/// <b>⇒ 故序号必须分到「项」，不能分到「片」。</b>若同一片的全部方法共用一个序号，
/// 则装箱器一旦把该片拆成两批（或让它与邻片合并），两批的 <c>Min</c> 就可能相同
/// ⇒ 执行器抛异常。反之，只要**每一项的序号互不相同**，则任意两批（项集不相交）的
/// <c>Min</c> 必然不同——因为最小值本身属于该批，而不相交集合的元素必不相同。
/// 本类型据此把序号分到项，并以**分片序**为主键、片内顺序为次键，
/// 使同一片的项在全局序中连续（分片的「分组」语义得以保留为邻接性）。
/// </para>
/// <para>
/// 序号空间划成**两个不相交的带**：
/// <list type="number">
/// <item><b>计划带</b> <c>[0, PlannedItemCount)</c>：计划覆盖的方法，逐项唯一。</item>
/// <item><b>回退带</b> <c>[PlannedItemCount, int.MaxValue]</c>：计划未覆盖的工作项
///   （例如调用图阶段处理的是调用/属性引用，不是方法根），由
///   <see cref="ComputeFallbackShardOrder"/> 分配。</item>
/// </list>
/// 两带不相交 ⇒ 无论计划覆盖多少项，全部 <c>ShardOrder</c> 都<b>唯一</b>。
/// </para>
/// <para>
/// 无计划（<c>null</c>）时消费方只走回退带的**退化形态**：单文档直接返回局部序号
/// （改造前行为，逐字不变）；多文档按文档序号错开（修复跨文件局部序号碰撞）。
/// </para>
/// </summary>
/// <remarks>
/// 刻意用 <c>class</c> 而非 <c>record</c>：构造函数要把
/// （文件, 方法序号）→ 全局项序号 的索引**建一次**，供每个工作项 O(1) 查询。
/// 若用 <c>record</c> 的按需计算属性，每个项都要重新遍历全部计划（O(项数 × 片数)）。
/// </remarks>
public sealed class CpgWorkShardPlan
{
    private readonly Dictionary<string, Dictionary<int, int>> _itemOrdersByFile;

    /// <summary>以各文件的分片分配构造计划；索引在建构时一次建好。</summary>
    /// <param name="files">各文件的分片分配；顺序即文件序。</param>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> 或其中元素为 null。</exception>
    /// <exception cref="ArgumentException">
    /// 同一文件路径重复出现，或同一文件内同一方法序号出现在多个分片
    /// （后者会让「每个方法恰好属于一个分片」的守恒不成立，且破坏序号唯一性）。
    /// </exception>
    public CpgWorkShardPlan(IReadOnlyList<CpgWorkShardAssignment> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = files;

        _itemOrdersByFile = new Dictionary<string, Dictionary<int, int>>(
          files.Count,
          StringComparer.Ordinal);
        var nextOrder = 0;
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            ArgumentNullException.ThrowIfNull(file);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.SourceFilePath);

            var ordersByMethod = new Dictionary<int, int>();
            for (var shardIndex = 0; shardIndex < file.Shards.Count; shardIndex++)
            {
                var shard = file.Shards[shardIndex];
                ArgumentNullException.ThrowIfNull(shard);
                for (var methodIndex = 0; methodIndex < shard.MethodOrders.Count; methodIndex++)
                {
                    var methodOrder = shard.MethodOrders[methodIndex];
                    ArgumentOutOfRangeException.ThrowIfNegative(methodOrder);
                    if (!ordersByMethod.TryAdd(methodOrder, nextOrder))
                    {
                        throw new ArgumentException(
                          $"文件 '{file.SourceFilePath}' 的方法序号 {methodOrder} 出现在多个分片中；" +
                          "每个方法必须恰好属于一个分片，否则序号唯一性不成立。",
                          nameof(files));
                    }

                    nextOrder += 1;
                }
            }

            if (!_itemOrdersByFile.TryAdd(file.SourceFilePath, ordersByMethod))
            {
                throw new ArgumentException(
                  $"文件路径 '{file.SourceFilePath}' 在分片计划中重复出现。",
                  nameof(files));
            }
        }

        PlannedItemCount = nextOrder;
    }

    /// <summary>空计划：任何查询都不命中，消费方走回退带。</summary>
    public static CpgWorkShardPlan Empty { get; } =
      new(Array.Empty<CpgWorkShardAssignment>());

    /// <summary>各文件的分片分配；顺序即文件序（决定全局序的先后）。</summary>
    public IReadOnlyList<CpgWorkShardAssignment> Files { get; }

    /// <summary>
    /// 计划覆盖的方法总数，同时是**计划带的上界**：计划序号取值
    /// <c>[0, PlannedItemCount)</c>，回退带从本值开始，两带不相交。
    /// </summary>
    public int PlannedItemCount { get; }

    /// <summary>
    /// 查询某文件某方法序号在全局调度序中的位置（落在 <c>[0, PlannedItemCount)</c>）。
    /// </summary>
    /// <remarks>
    /// 未命中是**正常**情形而非错误：并非每个工作项都由操作根派生。
    /// 调用方对未命中项应使用 <see cref="ComputeFallbackShardOrder"/>，
    /// <b>不得</b>沿用文件内局部序号——后者跨文件必然重复。
    /// </remarks>
    /// <param name="sourceFilePath">文件路径。</param>
    /// <param name="methodOrder">**文件内局部**方法序号。</param>
    /// <param name="itemOrder">命中时给出全局项序号。</param>
    /// <returns>命中返回 <c>true</c>，否则 <c>false</c>。</returns>
    public bool TryResolveItemOrder(
      string sourceFilePath,
      int methodOrder,
      out int itemOrder)
    {
        itemOrder = 0;
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return false;
        }

        return _itemOrdersByFile.TryGetValue(sourceFilePath, out var ordersByMethod) &&
               ordersByMethod.TryGetValue(methodOrder, out itemOrder);
    }

    /// <summary>
    /// 回退带的全局序号：<c>PlannedItemCount + 文档序号 × 步长 + 文件内局部序号</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>无计划且单文档时逐字退化为 <paramref name="localOrder"/></b>——这正是改造前的
    /// 行为，故全部既有单文件路径不受影响（含 <c>ShardOrder == StableOrder</c> 这一既有事实）。
    /// </para>
    /// <para>
    /// <b>为什么需要它（修掉的真实缺陷）：</b>跨文件装箱时若沿用文件内局部序号，
    /// 两个文件各自的 <c>0</c> 会相同；而批序号取批内 <c>Min</c>，
    /// 两个批就可能取得<b>相同</b>的序号，执行器随即抛
    /// <c>"WorkBatch shard orders must be unique."</c>
    /// </para>
    /// <para>
    /// 步长取 <c>(int.MaxValue - PlannedItemCount) / (documentCount + 1)</c>，使
    /// <c>PlannedItemCount + documentIndex × stride</c> 不溢出且各文件区间互不重叠，
    /// 且整体落在计划带之上。<paramref name="localOrder"/> 超出步长时<b>显式抛出</b>——
    /// 越界会破坏唯一性，而唯一性是执行器的硬契约，静默越界不可接受。
    /// </para>
    /// </remarks>
    /// <param name="documentIndex">文档在本次构建中的序号（0 起）。</param>
    /// <param name="documentCount">本次构建的文档总数。</param>
    /// <param name="localOrder">该文件内局部序号。</param>
    /// <returns>全局唯一的调度序号（落在回退带）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">序号为负，或文档数小于 1。</exception>
    /// <exception cref="InvalidOperationException">局部序号超出本文件可用步长，或序号空间不足。</exception>
    public int ComputeFallbackShardOrder(
      int documentIndex,
      int documentCount,
      int localOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(documentCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(localOrder);

        var bandStart = PlannedItemCount;

        // 唯一「逐字回到改造前」的分支：单文档且计划为空。
        // 此时 bandStart == 0，直接返回局部序号。有计划时**不得**走这里：
        // 计划带已占用 [0, bandStart)，返回局部序号会与之重叠。
        if (documentCount == 1 && bandStart == 0)
        {
            return localOrder;
        }

        var available = int.MaxValue - bandStart;
        if (available <= 0)
        {
            throw new InvalidOperationException(
              $"分片计划已占用 {bandStart} 个序号，回退带无剩余空间；无法为多文档编码唯一序号。");
        }

        var stride = available / (documentCount + 1);
        if (stride < 1)
        {
            throw new InvalidOperationException(
              $"文档数 {documentCount} 过大，无法为每个文件分配非空序号区间。");
        }

        if (localOrder >= stride)
        {
            throw new InvalidOperationException(
              $"文件内局部序号 {localOrder} 超出跨文件步长 {stride}；" +
              "继续编码会让相邻文件的序号区间重叠，破坏执行器要求的全局唯一性。");
        }

        return checked(bandStart + (documentIndex * stride) + localOrder);
    }
}
