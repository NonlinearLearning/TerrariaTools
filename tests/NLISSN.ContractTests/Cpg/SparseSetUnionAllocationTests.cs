using System.Reflection;
using NLCPG.Builder;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// <c>SparseSetStore.Union</c> 的**集合语义**、**共享别名**与**分配账本**契约。
///
/// 本类与 <c>NLCPGDataFlowSparseSetTests</c> 分工不同：那一类经公开构图验证
/// "溢出不截断、DataFlow 边不丢"，本类验证并集算子本身的行为与分配，
/// 不替代完整构图契约。
///
/// **为什么要用内部存储入口**：分配账本（"稳态零分配"、"只创建一份精确长度 spill"）
/// 无法从公开构图观测。故按执行计划 Task 1.5 把 <c>SparseSetStore</c> 由
/// <c>private</c> 提升为 <c>internal</c>（**位置与形状不变**），经
/// <c>src/NLCPG/Properties/AssemblyInfo.cs</c> 既有的
/// <c>InternalsVisibleTo("RoslynDeletionPrototype.ContractTests")</c> 友元访问；
/// 未新增公开 API。
///
/// 唯一仍走反射的是 <c>DefinitionFact</c>：它是 <c>private</c> 嵌套类型，
/// 而 <c>ApplyDefinitionTransfer</c> 的签名要求它。这与
/// <c>DataFlowDiagnosticFallbackTests</c> 既有的反射缝一致，**只用于播种**，
/// 播种永远发生在测量区间之外。
/// </summary>
public sealed class SparseSetUnionAllocationTests
{
    private const BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type FactType = typeof(NLCPGBuilder)
        .GetNestedType("DefinitionFact", BindingFlags.NonPublic)!;

    private static readonly MethodInfo TransferMethod = typeof(NLCPGBuilder.SparseSetStore)
        .GetMethod("ApplyDefinitionTransfer", AllInstance)!;

    private static readonly FieldInfo OverflowField = typeof(NLCPGBuilder.SparseSetStore)
        .GetField("_overflow", AllInstance)!;

    /// <summary>
    /// 空的定义事实清单。转移里的"冲突清除"分支形如
    /// <c>ordinal &lt; definitionFactsByDefinitionOrdinal.Count &amp;&amp; FactsConflict(...)</c>，
    /// 清单为空时恒为假，因此播种只做"按序插入"，不删任何元素。
    /// </summary>
    private static readonly Array NoFacts = Array.CreateInstance(FactType, 0);

    /// <summary>任意占位事实；在 <see cref="NoFacts"/> 下不会被读取。</summary>
    private static readonly object AnyFact = CreateFact();

    // ------------------------------------------------------------ 集合语义 oracle

    /// <summary>
    /// 空 / 单元素 / 相等 / 双向包含 / 相交 / 不交 / 自合并 / 基数 1、2、8、9
    /// 以及较大输入，结果与 <see cref="SortedSet{T}"/> 的完整升序序列逐一比较。
    ///
    /// oracle 刻意用 <see cref="SortedSet{T}"/> 而不是抄一份生产归并算法：
    /// 抄算法会让测试与实现同时错，无法互相校验。
    /// </summary>
    [Theory]
    [InlineData(new int[0], new int[0])]
    [InlineData(new[] { 3 }, new int[0])]
    [InlineData(new int[0], new[] { 3 })]
    [InlineData(new[] { 3 }, new[] { 3 })]
    [InlineData(new[] { 1, 2, 3 }, new[] { 1, 2, 3 })]
    [InlineData(new[] { 1, 2, 3 }, new[] { 2, 3 })]
    [InlineData(new[] { 2, 3 }, new[] { 1, 2, 3 })]
    [InlineData(new[] { 1, 3, 5, 7 }, new[] { 2, 4, 6, 8 })]
    [InlineData(new[] { 1, 3, 5, 7 }, new[] { 3, 5, 9 })]
    [InlineData(new[] { 4 }, new[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    // 以下两条是**读写别名**的定向用例：源最小元素小于目标最小元素，
    // 于是并集首元素由源贡献，第二遍归并会把目标内联槽覆盖成源的最小值；
    // 若实现边写内联边读同一槽，目标原有的最小元素会被自己的新值顶掉（重复/丢元素）。
    [InlineData(new[] { 5, 6, 7 }, new[] { 1, 2 })]
    [InlineData(new[] { 4, 6, 8 }, new[] { 1, 2, 3 })]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 8 })]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, new[] { 8, 9 })]
    [InlineData(new[] { 0, 2, 4, 6, 8, 10, 12, 14, 16 }, new[] { 1, 3, 5, 7, 9, 11, 13, 15 })]
    [InlineData(
      new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
      new[] { 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35 })]
    public void Union_MatchesSortedSetOracle(int[] targetValues, int[] sourceValues)
    {
        var store = NewStore(nodeCount: 8);
        PreallocateOverflow(store, capacity: 64);
        Seed(store, 0, targetValues);
        Seed(store, 1, sourceValues);

        store.Union(0, 1);

        var expected = new SortedSet<int>(targetValues);
        expected.UnionWith(sourceValues);
        Assert.Equal(expected.ToArray(), Sequence(store, 0));
        Assert.Equal(expected.Count, store.CountOf(0));

        // 源槽是共享旧 spill 的可能持有者：并集不得改动输入。
        Assert.Equal(sourceValues.Order().ToArray(), Sequence(store, 1));
        Assert.Equal(sourceValues.Length, store.CountOf(1));
    }

    /// <summary>自合并是恒等操作，且无需读取任何元素。</summary>
    [Fact]
    public void Union_WithItself_IsIdentity()
    {
        var store = NewStore(nodeCount: 8);
        PreallocateOverflow(store, capacity: 64);
        Seed(store, 3, [0, 1, 2, 3, 4, 5, 6, 7, 8]);

        store.Union(3, 3);

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, Sequence(store, 3));
        Assert.Equal(9, store.CountOf(3));
    }

    // ------------------------------------------------------------ 共享别名不变式

    /// <summary>
    /// <c>Copy</c> 共享不可变 spill 引用，故 A、B 两槽可以指向同一个 spill 数组。
    /// 合并 A 之后 B 的完整升序内容必须逐元素不变——**旧 spill 永不被写入**。
    ///
    /// 这是本轮最容易静默出错的一条：若第二遍归并直接写目标 spill（或误持源 spill 引用），
    /// B 会被污染，而 fixpoint 仍然"收敛"，只是结果错了。
    /// </summary>
    [Fact]
    public void Union_OnSlotsSharingOneSpill_LeavesTheOtherSlotUntouched()
    {
        var store = NewStore(nodeCount: 8);
        PreallocateOverflow(store, capacity: 64);
        Seed(store, 0, [1, 2, 3, 4, 5]);
        Seed(store, 1, [100, 200, 300]);

        // Copy 后槽 0 与槽 2 共享同一个 spill。
        store.Copy(2, 0);
        var shared = Sequence(store, 2);

        store.Union(0, 1);

        Assert.Equal(shared, Sequence(store, 2));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 100, 200, 300 }, Sequence(store, 0));
    }

    /// <summary>合并的**源槽**自身与目标槽共享 spill 时，源槽也必须保持不变。</summary>
    [Fact]
    public void Union_WithSharedSourceSpill_LeavesSourceSlotUntouched()
    {
        var store = NewStore(nodeCount: 8);
        PreallocateOverflow(store, capacity: 64);
        Seed(store, 0, [1, 2, 3, 4, 5]);
        store.Copy(1, 0);
        Seed(store, 2, [7, 8, 9]);

        store.Union(0, 2);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Sequence(store, 1));
    }

    /// <summary>
    /// 复现 fixpoint 的真实交错序列：多前驱求并进 in 槽 → Copy 到 updated → 再合并。
    /// 断言前驱槽（即被多方共享的旧 spill 持有者）全程不变，且结果与 oracle 一致。
    /// </summary>
    [Fact]
    public void FixpointInterleaving_KeepsPredecessorSlotsAndMatchesOracle()
    {
        var store = NewStore(nodeCount: 8);
        PreallocateOverflow(store, capacity: 64);
        int[][] predecessors = [[0, 4, 8], [2, 4, 6], [1, 2, 3, 4, 5]];
        for (var ordinal = 0; ordinal < predecessors.Length; ordinal += 1)
        {
            Seed(store, ordinal, predecessors[ordinal]);
        }

        var incoming = store.IncomingScratchSlot;
        var updated = store.UpdatedScratchSlot;
        store.Clear(incoming);
        for (var ordinal = 0; ordinal < predecessors.Length; ordinal += 1)
        {
            store.Union(incoming, ordinal);
        }

        store.Copy(updated, incoming);

        // Copy 之后 updated 与 incoming 共享 spill；对 incoming 再合并不得影响 updated。
        var updatedBefore = Sequence(store, updated);
        store.Union(incoming, 0);
        Assert.Equal(updatedBefore, Sequence(store, updated));

        var expected = new SortedSet<int>();
        foreach (var values in predecessors)
        {
            expected.UnionWith(values);
        }

        Assert.Equal(expected.ToArray(), Sequence(store, updated));

        for (var ordinal = 0; ordinal < predecessors.Length; ordinal += 1)
        {
            Assert.Equal(predecessors[ordinal], Sequence(store, ordinal));
        }
    }

    // ------------------------------------------------------------ 分配账本

    /// <summary>
    /// 空源、自合并、结果不变（target ⊇ source）、可 Copy 的包含关系（target ⊂ source）
    /// 四条路径在稳态下**每次并集的新增堆分配为 0**。
    /// </summary>
    [Fact]
    public void Union_SteadyState_AllocatesNothingOnEmptySelfSubsetAndCopyPaths()
    {
        const int Groups = 32;
        var store = NewStore(nodeCount: (Groups + 1) * 4);
        PreallocateOverflow(store, capacity: 512);
        for (var group = 0; group <= Groups; group += 1)
        {
            var slot = group * 4;
            Seed(store, slot, [1, 2, 3]);
            Seed(store, slot + 2, [1, 2, 3, 4, 5]);
        }

        void Exercise(int group)
        {
            var slot = group * 4;
            store.Union(slot, slot + 1);      // sourceCount == 0 → 直接返回
            store.Union(slot, slot);          // 自合并 → 直接返回
            store.Union(slot, slot + 2);      // target ⊂ source → Copy
            store.Union(slot + 3, slot);      // targetCount == 0 → Copy
        }

        // 预热组与测量组槽位互不相交：既触发 JIT，又不污染被测状态。
        Exercise(Groups);

        var measured = MeasureAllocatedBytes(() =>
        {
            for (var group = 0; group < Groups; group += 1)
            {
                Exercise(group);
            }
        });

        Assert.Equal(0L, measured);
    }

    /// <summary>
    /// "结果不变"路径单列测量：并集基数等于目标基数时结果完全不变，
    /// 连 spill 引用都不该被替换。它是真实 fixpoint 里最高频的非空双输入合并。
    ///
    /// 该操作天然幂等，故可直接用同一批槽位预热一次，再测量第二次。
    /// </summary>
    [Fact]
    public void Union_UnchangedResult_AllocatesNothing()
    {
        const int Groups = 32;
        var store = NewStore(nodeCount: Groups * 2 + 2);
        PreallocateOverflow(store, capacity: 512);
        for (var group = 0; group < Groups; group += 1)
        {
            Seed(store, group * 2, [0, 1, 2, 3, 4, 5, 6, 7]);
            Seed(store, group * 2 + 1, [2, 3]);   // 真子集 → unionCount == targetCount
        }

        void Sweep()
        {
            for (var group = 0; group < Groups; group += 1)
            {
                store.Union(group * 2, group * 2 + 1);
            }
        }

        Sweep();
        var measured = MeasureAllocatedBytes(Sweep);

        Assert.Equal(0L, measured);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Sequence(store, 0));
    }

    /// <summary>
    /// 改变且溢出的并集**只**创建一份精确长度的最终 spill：不存在原实现的
    /// <c>merged</c>、<c>source</c>、<c>target</c> 中间数组。
    ///
    /// 判据是**与同一运行时实测的 <c>new int[n]</c> 分配量逐字节相等**，
    /// 而不是写死一个依赖对象头与对齐的魔数。溢出字典预先扩容，其成本不落在样本内。
    /// </summary>
    [Fact]
    public void Union_ChangedOverflow_AllocatesExactlyOneFinalSpill()
    {
        const int Groups = 16;
        const int UnionCount = 5;
        var store = NewStore(nodeCount: (Groups + 1) * 2);
        PreallocateOverflow(store, capacity: 512);
        for (var group = 0; group <= Groups; group += 1)
        {
            Seed(store, group * 2, [0, 1, 2]);
            Seed(store, group * 2 + 1, [10, 11]);
        }

        void Exercise(int group)
        {
            store.Union(group * 2, group * 2 + 1);
        }

        Exercise(Groups);

        var oneSpill = ArrayAllocationBytes(UnionCount - 1);
        var measured = MeasureAllocatedBytes(() =>
        {
            for (var group = 0; group < Groups; group += 1)
            {
                Exercise(group);
            }
        });

        Assert.Equal(Groups * oneSpill, measured);
        Assert.Equal(new[] { 0, 1, 2, 10, 11 }, Sequence(store, 0));
    }

    /// <summary>
    /// 冷态：同一 store 的**第一次**溢出合并除最终 spill 外，还要额外创建
    /// <c>_overflow</c> 字典。该成本在此单列，**不**伪称"永远只分配一个对象"。
    ///
    /// 两侧各只有一个内联元素时都没有溢出条目，故字典确实是首次创建。
    /// </summary>
    [Fact]
    public void Union_FirstOverflow_AlsoCreatesTheOverflowDictionary()
    {
        var store = NewStore(nodeCount: 4);
        Seed(store, 0, [5]);
        Seed(store, 1, [9]);

        var measured = MeasureAllocatedBytes(() => store.Union(0, 1));

        var oneSpill = ArrayAllocationBytes(1);
        Assert.True(
          measured > oneSpill,
          $"冷态应额外承担 _overflow 字典成本；实测 {measured} 字节，单份 spill 为 {oneSpill} 字节");
        Assert.Equal(new[] { 5, 9 }, Sequence(store, 0));
    }

    // ------------------------------------------------------------ 播种与测量辅助

    private static NLCPGBuilder.SparseSetStore NewStore(int nodeCount)
    {
        return new NLCPGBuilder.SparseSetStore(nodeCount);
    }

    /// <summary>
    /// 预置溢出字典容量，使测量区间内不出现字典扩容分配。
    /// 它只把"字典首次分配/扩容"这一独立成本移出样本，不改变被测的并集分配行为。
    /// </summary>
    private static void PreallocateOverflow(NLCPGBuilder.SparseSetStore store, int capacity)
    {
        OverflowField.SetValue(store, new Dictionary<int, int[]>(capacity));
    }

    /// <summary>
    /// 以升序序数播种。<c>ApplyDefinitionTransfer</c> 在空前驱事实下按序插入，
    /// 是生产里唯一能在空槽上构造非空集合的入口。
    /// </summary>
    private static void Seed(NLCPGBuilder.SparseSetStore store, int slot, int[] ascending)
    {
        foreach (var ordinal in ascending)
        {
            object?[] arguments = [slot, ordinal, AnyFact, NoFacts];
            TransferMethod.Invoke(store, arguments);
        }
    }

    private static int[] Sequence(NLCPGBuilder.SparseSetStore store, int slot)
    {
        return store.Enumerate(slot).ToArray();
    }

    private static long MeasureAllocatedBytes(Action operation)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        operation();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// 在**本运行时**实测一份 <c>new int[length]</c> 的分配量，
    /// 使精确断言不依赖对象头大小与对齐假设。
    /// </summary>
    private static long ArrayAllocationBytes(int length)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var probe = new int[length];
        var after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(probe);
        return after - before;
    }

    private static object CreateFact()
    {
        var constructor = FactType
          .GetConstructors(AllInstance)
          .Single(candidate => candidate.GetParameters().Length == 4);
        object?[] arguments = ["fact", null, "local", null];
        return constructor.Invoke(arguments);
    }
}
