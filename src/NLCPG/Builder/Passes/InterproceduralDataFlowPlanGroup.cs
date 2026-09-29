using System.Runtime.CompilerServices;
using NLCPG.Model;

namespace NLCPG.Builder.Passes;

/// 跨过程桥计划的【计划数组载体】——一个薄的精确分配包装。
///
/// 【为什么不能直接用 List】<see cref="List{T}"/> 的构造函数【自己 new T[]】，
/// 没有任何公开 API 能把外部数组交给它当后备存储。本类型存在的全部意义就是：
/// 让调用方能在【生成之前】按精确上界分配后备数组，并在之后只读回读。
///
/// 【P03 池化已被 ⑥ 取代（2026-09-27）】本类型原先是 `ArrayPool<…>.Shared` 的租借包装，
/// 因为 216 B 的元素宽让"≥394 条"就落进 LOH。⑥ 把元素宽降到 8 B 后 LOH 门槛升到
/// 10,626 条，而实测最大单组只有 5,050 条（源码注记 5,878）⇒ 池化在已实测语料上
/// **不可达**，收益归零。故按 ⑥ §4.4 的处置【显式退役】池化分支：删除 `ArrayPool`、
/// `PoolMinimumCapacity` 与 `RentCount`/`ReturnCount` 账本，只保留精确分配。
/// 这不是"顺手删除"——⑤ 的执行文档已就地补记"被 ⑥ 取代"。
///
/// 【为什么仍是 class 而不是 struct】组头是 readonly struct，按值复制极常见
/// （窗口槽、局部变量）。若本类型是 struct，复制出的副本会各持一份数组引用，
/// 而 class 只有一个实例，组头宽度也只有【一个引用】。附带好处：组头宽度不变
/// （原来字段是 List&lt;T&gt; 引用，现在仍是 class 引用）。
internal sealed class InterproceduralPlanBuffer
{
    // 非 readonly：兜底扩容会换数组。每条计划的"当前后存储"恒与 Count 匹配。
    private InterproceduralPlanRef[] _array;

    private readonly int _initialCapacity;

    private InterproceduralPlanBuffer(InterproceduralPlanRef[] array, int initialCapacity)
    {
        _array = array;
        _initialCapacity = initialCapacity;
    }

    internal int Count { get; private set; }

    internal int Capacity => _array.Length;

    /// 共享的空载体。两段式组头里「没有共享段」是常态（未共享的组、非正预算分支），
    /// 用共享实例可避免每组各分配一个空包装对象。
    internal static InterproceduralPlanBuffer Empty { get; } =
      new(Array.Empty<InterproceduralPlanRef>(), 0);

    /// 构造时的请求容量（= min(原上界, 预算)）。精确分配下它恒等于 <see cref="Capacity"/>，
    /// 但仍在此刻固化而不事后反推：账本断言的判别量必须来自请求时刻，不能依赖分配策略。
    internal int InitialCapacity => _initialCapacity;

    /// 按请求容量精确分配。零容量走共享空数组，不分配。
    internal static InterproceduralPlanBuffer Create(int capacity)
    {
        return new InterproceduralPlanBuffer(
          capacity == 0
            ? Array.Empty<InterproceduralPlanRef>()
            : new InterproceduralPlanRef[capacity],
          capacity);
    }

    /// 读一条计划引用。按值返回，与原先 `List&lt;T&gt;[i]` 的语义逐位相同。
    internal InterproceduralPlanRef this[int index] => _array[index];

    internal void Add(InterproceduralPlanRef plan)
    {
        if (Count == _array.Length)
        {
            Grow(Count + 1);
        }

        _array[Count] = plan;
        Count += 1;
    }

    private void Grow(int required)
    {
        // List<T> 的倍增语义：容量为 0 时起步 4，否则翻倍。
        // 正常路径下调用方已按精确上界预分配，故这里只是兜底（容量 0 的组也会走到）。
        var next = Math.Max(required, _array.Length == 0 ? 4 : _array.Length * 2);
        var grown = Create(next);
        Array.Copy(_array, grown._array, Count);
        grown.Count = Count;
        _array = grown._array;
        // _initialCapacity 保持构造时刻的请求值（readonly），不随兜底扩容改变。
    }

    /// 仅非正预算兼容分支使用：逐位复刻 <see cref="List{T}.RemoveRange"/> 的异常与结果，
    /// 以免把既有的 ArgumentOutOfRangeException 静默变成"跳过"。
    internal void RemoveRange(int index, int count)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (Count - index < count)
        {
            throw new ArgumentException(
              "Offset and length were out of bounds for the array or count is greater than "
                + "the number of elements from index to the end of the source collection.");
        }

        if (count > 0)
        {
            Array.Copy(_array, index + count, _array, index, Count - index - count);
            Count -= count;
        }
    }

    /// 兼容分支保留原调用点。整轮删空（预算 0）之后发布器会立刻抛
    /// `ArgumentOutOfRangeException`，故缩容在任何可观测面上都不成立；
    /// 这里刻意不实现它 —— 那是本轮范围之外的语义变更，不是"顺手省略"。
    internal void TrimExcess()
    {
    }
}

/// 跨过程桥计划的【组头】：一个调用点拥有的全部桥计划。
///
/// 为什么需要这一层（方案 B）：在此之前，CallSiteNode、TargetMethodNode 与
/// StableCallSiteOrder 在**每条计划**里各存一份。而除 CallSiteNode 被发布器按组读取一次外，
/// 三者在本发布路径上没有任何字段读取方，却让每条计划各背一个 104 B 的额外 NLCPGNode。
/// 提到组头后，每条计划只余下引导 AddEdge 所需的那一个池内序号。
///
/// 【两段式：跨调用点复用 ArgumentToParameter 段】
///   · <see cref="SharedArgumentPlans"/> —— ArgumentToParameter 前缀。它的内容只由
///     「目标方法 + 全局预算」决定，与调用点无关 ⇒ 同一目标方法的全部调用点共享同一份数组，
///     把累计载体分配从 c·(c·p) 降到 c·p。按引用持有，故共享本身不产生任何复制。
///   · <see cref="GroupTailPlans"/> —— 本组私有尾段
///     （MethodReturnToCallResult + 仅在首个调用点出现的 ReturnToMethodReturn）。
///   两段按「共享段在前、尾段在后」拼接后与改造前【逐位相同】，这是边序冻结 oracle 的前提。
///
/// 【PlanIndex 是两段共用的统一编号空间】<see cref="PlanAt"/> 负责这一映射。
/// 若让两段各自从 0 编号，同一组内就会出现重复下标，而 PlanIndex 同时是组内排序的
/// 【末键】（List&lt;T&gt;.Sort 不稳定）⇒ 边序失去确定性，冻结 oracle 会红。
///
/// 所有权与生命周期：
///   · 组头在计划构造上界确定后建立，因此构造期就能给出精确容量；
///   · 两段在【进入发布窗口后】均不得再追加、重排或跨组改写（PlanIndex 绑定原始插入序）；
///   · 共享段的生命周期是【整个文档】（由记忆化容器持有），尾段的生命周期才是【本组】；
///   · 发布器只读组头与两段数组。
internal readonly struct InterproceduralDataFlowPlanGroup(
  NLCPGNode callSiteNode,
  int stableCallSiteOrder,
  InterproceduralPlanBuffer sharedArgumentPlans,
  InterproceduralPlanBuffer groupTailPlans)
{
    /// 该组所属调用点的完整节点快照。发布器由它构造一次调用点上下文。
    internal NLCPGNode CallSiteNode { get; } = callSiteNode;

    /// 原调用点遍历下标。发布路径当前无读取方；保留在组头是为了不丢失「原调用点顺序」这一
    /// 所有权事实（窗口 slot 次序与它相同，但那是窗口实现细节，不是可依赖的来源）。
    internal int StableCallSiteOrder { get; } = stableCallSiteOrder;

    /// 共享的 ArgumentToParameter 前缀：同一目标方法的全部调用点共用同一实例。
    internal InterproceduralPlanBuffer SharedArgumentPlans { get; } = sharedArgumentPlans;

    /// 本组私有的尾段计划（MethodReturnToCallResult + ReturnToMethodReturn）。
    internal InterproceduralPlanBuffer GroupTailPlans { get; } = groupTailPlans;

    /// 两段之和。窗口行数、窗口组数上界与派生的 PeakWindowRows 都读它，
    /// 故复用【不改变】任何与窗口规模相关的读数。
    internal int Count => SharedArgumentPlans.Count + GroupTailPlans.Count;

    /// 组内统一下标 → 惰性载体。下标空间为「共享段在前、尾段在后」，
    /// 与改造前单缓冲的插入序逐位相同。
    internal InterproceduralPlanRef PlanAt(int planIndex)
    {
        var sharedCount = SharedArgumentPlans.Count;
        return planIndex < sharedCount
          ? SharedArgumentPlans[planIndex]
          : GroupTailPlans[planIndex - sharedCount];
    }
}

/// 跨过程计划发布窗口的容量账本（执行文档第 3 节）。
///
/// 存在的理由：本项的收益门槛写在**保留容量**上，而保留容量既不是元素宽度、也不是累计分配量——
/// 它是"发布之后仍然被 List&lt;T&gt;.Capacity 扣住、却已无元素使用的字节"。
/// 没有这份账，"取消无条件 TrimExcess"与"64 槽合计治理"都只是无可观测的空承诺。
///
/// 记账口径（不得混算）：
///   · <see cref="PlanCapacitySlackBytes"/> 只统计【正预算】组的计划列表 slack（Capacity - Count）；
///     非正预算是兼容分支，不计入收益。
///   · <see cref="SortBufferRetainedBytes"/> 是**治理之后**保留的排序数组字节，
///     <see cref="SortBufferPeakRetainedBytes"/> 是**治理之前**的同期读数；两者之差即治理释放的容量。
///     二者都不是当前活跃行数，也不是进程内存。
///
/// 放在本文件而不是另开文件：执行文档第 1 节把产品范围限为「计划类型、Builder 相关方法，
/// 最多新增一个组载体文件」。账本与组载体同属本次缩窄的观测面，故合并到这一个新文件内。
///
/// 【⑤ 池化账本已随 ⑥ 退役】原 `PlanBufferRentCount`/`PlanBufferReturnCount` 两个字段已删除：
/// 载体不再进 LOH，`ArrayPool` 分支不存在，"漏归还"这个失败模式随之消失。
internal readonly record struct InterproceduralPlanCapacityLedger(
  long PlanCapacitySlackBytes,
  int PlanCapacitySlackGroups,
  int PlanTrimCount,
  long SortBufferRetainedBytes,
  long SortBufferPeakRetainedBytes,
  int SortBufferReclaimedSlots,
  int SortBufferClearedSlots,
  long SortBufferResidualRows,
  int FlushCount,
  int PeakWindowGroups,
  long PeakWindowRows,
  long PlanCountTotal,
  long PlanCapacityTotal,
  int MaxPlanCountPerGroup,
  int MaxPlanCapacityPerGroup,
  long PlanInitialCapacityTotal,
  int MaxPlanInitialCapacityPerGroup,
  long SharedArgumentPlanCountTotal,
  long SharedArgumentPlanCapacityTotal,
  long SharedArgumentInitialCapacityTotal,
  int SharedArgumentMethodCount)
{
    internal static InterproceduralPlanCapacityLedger Empty { get; } =
      new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// 上述账本的累加器，只在本 pass 的单次串行构造/发布路径上写入。
/// internal 而非 private：容量治理的接线测试需要直接驱动它。
internal sealed class InterproceduralPlanCapacityLedgerBuilder
{
    internal long PlanCapacitySlackBytes { get; set; }

    internal int PlanCapacitySlackGroups { get; set; }

    internal int PlanTrimCount { get; set; }

    /// 治理之后仍保留的排序数组字节（各槽 Capacity 之和 × 行宽）。
    internal long SortBufferRetainedBytes { get; set; }

    /// 各槽历史上曾被扣住的排序数组字节峰值。保留它的唯一目的是让"治理确实发生了"
    /// 可被观测：若峰值 == 保留值，说明这次构建根本没触发回收，断言不该假装通过。
    internal long SortBufferPeakRetainedBytes { get; set; }

    internal int SortBufferReclaimedSlots { get; set; }

    internal int SortBufferClearedSlots { get; set; }

    /// 发布段执行 `rows.Clear()` **之后**实测到的残留行数合计。
    ///
    /// 存在的理由（本项唯一能真正观测「强引用可达性」的量）：`SortBufferClearedSlots`
    /// 是"计数器被加过"，而本字段是"清空之后槽里到底还剩几行"。正常路径恒为 0；
    /// 若有人删掉 `rows.Clear();` 而保留计数器，该值立刻 > 0 ⇒ 断言当场失败。
    /// 旧版仅断言计数器，实测对「删掉 Clear 只留计数器」这一变异**无判别力**（39/39 仍通过）。
    internal long SortBufferResidualRows { get; set; }

    /// 已冲刷的窗口数。用于证明夹具真的跨越了 64 组 / 131072 行的窗口边界，
    /// 而不是"配方看起来很大"。这是窗口密封账，不是收益读数。
    internal int FlushCount { get; set; }

    internal int PeakWindowGroups { get; set; }

    internal long PeakWindowRows { get; set; }

    /// 正预算组实际保留的计划条数与容量之和（元素数，未乘元素宽度）。
    /// 验收判据"溢出组的保留计划数/容量不超过 B"直接读这两个值。
    internal long PlanCountTotal { get; set; }

    internal long PlanCapacityTotal { get; set; }

    internal int MaxPlanCountPerGroup { get; set; }

    internal int MaxPlanCapacityPerGroup { get; set; }

    /// 各组【初始容量】之和与其最大值。这是前缀收集与「先全建再截断」的**唯一判别量**：
    /// 旧实现在截断后同样调用 TrimExcess，故【最终】Capacity 也会回到 B —— 只看最终容量
    /// 或最终条数，新旧代码给出相同的账，那样的断言没有判别力。
    /// 真正被消除的是**生成期曾按上界分配过的那份数组**，它体现在初始容量上。
    ///
    /// 【两段式后只统计私有尾段】共享段的初始容量是【每方法一次】，若并入本字段，
    /// 「前缀收集」的判别力会被共享段的份额稀释。共享段另行记入
    /// <see cref="SharedArgumentInitialCapacityTotal"/>（这也是它必须独立成字段的理由之一）。
    internal long PlanInitialCapacityTotal { get; set; }

    internal int MaxPlanInitialCapacityPerGroup { get; set; }

    /// 共享 ArgumentToParameter 段：条数 / 容量 / 初始容量三项合计。
    ///
    /// 【为什么不并入上面三个字段 —— 重复计数陷阱】共享段【只有一份】。
    /// 若让同一目标方法的 c 个组各把同一份共享 buffer 累加一次，
    /// 就会把它虚增 c 倍（本该 c·p，账上却记成 c²·p），
    /// 于是本项的收益在账本上与改造前【无差别】，完全不可观测。
    /// ⇒ 共享段必须按「每方法一份」单独累加，且必须能由 N1/N3 断言钉死。
    internal long SharedArgumentPlanCountTotal { get; set; }

    internal long SharedArgumentPlanCapacityTotal { get; set; }

    internal long SharedArgumentInitialCapacityTotal { get; set; }

    /// 真正建过共享段的目标方法数。它是「按方法计一次」这件事的直接读数：
    /// 若有人把构造挪进逐调用点路径，本计数会退化成调用点数（或 0，若从不记账）。
    internal int SharedArgumentMethodCount { get; set; }

    /// 记一个组的账。
    ///
    /// 【两种口径，不得混用】<paramref name="retainedCount"/>/<paramref name="retainedCapacity"/>
    /// 是【整组】（共享段 + 尾段）的读数，只用于每组最大值；<paramref name="tailCount"/> 等三项
    /// 是【尾段】读数，按组累加。共享段不进这里 —— 它按方法记一次（见
    /// <see cref="ObserveSharedArgumentSegment"/>）。
    ///
    /// 为什么合计口径取「尾段 + 每方法一份共享段」：既有冻结断言
    /// `PlanCapacityTotal >= PlanCountTotal` 与 `capacityOverCount >= 1.0` 要求两者【同口径】；
    /// 若条数按「每组消耗整桶」（c²·p）而容量按「实际保留」（c·p），该不等式必红。
    /// 统一为「实际保留的载体份数」后不等式成立（每份都满足 容量 ≥ 条数），
    /// 且 `PlanCountTotal` 本身成为本项收益的判别量。
    internal void ObserveGroup(
      int retainedCount,
      int retainedCapacity,
      int tailCount,
      int tailCapacity,
      int tailInitialCapacity)
    {
        PlanCountTotal += tailCount;
        PlanCapacityTotal += tailCapacity;
        PlanInitialCapacityTotal += tailInitialCapacity;
        if (retainedCount > MaxPlanCountPerGroup)
        {
            MaxPlanCountPerGroup = retainedCount;
        }

        if (retainedCapacity > MaxPlanCapacityPerGroup)
        {
            MaxPlanCapacityPerGroup = retainedCapacity;
        }

        if (tailInitialCapacity > MaxPlanInitialCapacityPerGroup)
        {
            MaxPlanInitialCapacityPerGroup = tailInitialCapacity;
        }
    }

    /// 记一个【新建】共享段的账。调用点必须与「缓存未命中 ⇒ 真的构造了一份」一一对应
    /// （每方法至多一次），否则「按方法计一次」就只是注释里的说法。
    internal void ObserveSharedArgumentSegment(int count, int capacity, int initialCapacity)
    {
        SharedArgumentPlanCountTotal += count;
        SharedArgumentPlanCapacityTotal += capacity;
        SharedArgumentInitialCapacityTotal += initialCapacity;
        SharedArgumentMethodCount += 1;

        // 【口径②】共享段也必须进合计 —— 否则「按方法计一次」这件事就只体现在四个专用字段里，
        // 而既有冻结断言读的是 PlanCountTotal：在「共享段非空 + 尾段为空」的语料（正预算下
        // 完全可能，如单调用点、无返回边的方法）上它会读到 0，`PlanCountTotal > 0` 直接红。
        // 更重要的是：只有把共享段按【每方法一份】计入，PlanCountTotal 才等于"实际保留的载体
        // 份数"，与 PlanCapacityTotal 同口径，`容量 ≥ 条数` 才逐组成立而不是碰巧成立。
        PlanCountTotal += count;
        PlanCapacityTotal += capacity;

        // 共享段的 slack 也按「每方法一次」入账。若改到逐组累加，同一份共享段的 slack
        // 会被虚增 c 倍，与条数/容量的重复计数陷阱同型。
        PlanCapacitySlackBytes +=
          (long)(capacity - count) * Unsafe.SizeOf<InterproceduralPlanRef>();
        if (capacity > count)
        {
            PlanCapacitySlackGroups += 1;
        }
    }

    internal void ObserveWindow(int groups, long rows)
    {
        if (groups > PeakWindowGroups)
        {
            PeakWindowGroups = groups;
        }

        if (rows > PeakWindowRows)
        {
            PeakWindowRows = rows;
        }
    }

    internal InterproceduralPlanCapacityLedger ToLedger() => new(
      PlanCapacitySlackBytes,
      PlanCapacitySlackGroups,
      PlanTrimCount,
      SortBufferRetainedBytes,
      SortBufferPeakRetainedBytes,
      SortBufferReclaimedSlots,
      SortBufferClearedSlots,
      SortBufferResidualRows,
      FlushCount,
      PeakWindowGroups,
      PeakWindowRows,
      PlanCountTotal,
      PlanCapacityTotal,
      MaxPlanCountPerGroup,
      MaxPlanCapacityPerGroup,
      PlanInitialCapacityTotal,
      MaxPlanInitialCapacityPerGroup,
      SharedArgumentPlanCountTotal,
      SharedArgumentPlanCapacityTotal,
      SharedArgumentInitialCapacityTotal,
      SharedArgumentMethodCount);
}
