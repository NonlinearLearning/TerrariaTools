using System.Reflection;
using System.Runtime.CompilerServices;
using NLCPG.Builder;
using NLCPG.Builder.Passes;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

// M1（跨过程计划缩窄）的定向契约。
//
// 分工：边的【顺序】与【集合】等价已由 CpgInterproceduralEdgeOrderTests（冻结原始插入序）
// 与 CpgWorkBatchInterproceduralTests（跨 DOP oracle）承担，本文件不重复。
// 这里只补它们【看不见】的三件事：
//   ① 方案 B 的载体形状（计划只余两端点、调用点元数据在组头）；
//   ② 正预算的【前缀收集】——容量本身不得超出预算；
//   ③ 非正预算的既有异常边界，以及窗口/空闲槽的容量账本。
//
// 期望值来源：
//   · 旧计划宽度 428 B：Context/progress.md 记录的同运行时实测值。
//   · 预算 ≤ 0 抛 ArgumentOutOfRangeException：
//     Build/MemoryOptimization/M1/run-20260925-01/probe.txt 的改前实测值。
// 二者都是【冻结基线】，不是本文件推导出来的常量。
public sealed class InterproceduralPlanCompactionTests
{
    // 需要多调用点、且三种桥都出现（ArgumentToParameter / MethodReturnToCallResult /
    // ReturnToMethodReturn），否则"组"这个概念没有被真正压到。
    private const string Source = """
      using System;

      public sealed class OrderSample
      {
        public int Leaf(int value)
        {
          return value + 1;
        }

        public int Middle(int value)
        {
          var a = Leaf(value);
          var b = Leaf(a);
          return a + b;
        }

        public int Top(int value)
        {
          var a = Middle(value);
          var b = Middle(a);
          var c = Leaf(b);
          return a + b + c;
        }

        public int Wide(int value, int other)
        {
          return Leaf(value) + Middle(other) + Leaf(other) + Middle(value);
        }

        public int ReturnHeavy(int value)
        {
          var r = Middle(value);
          return r + Middle(r);
        }
      }
      """;

    // ── ① 载体形状 ────────────────────────────────────────────────────────────────

    // ⑥ 延迟物化：计划载体只保留"池里哪个序号 + 实参序"。
    // 这条断言是形状护栏：若有人把端点节点或桥种类塞回载体里（把中间结果重新物化成宽记录），
    // 哪怕行为暂时还是对的，这里也立即失败。
    [Fact]
    public void InterproceduralPlanRef_KeepsOnlyLazyHandlePayload()
    {
        var fieldNames = typeof(InterproceduralPlanRef)
          .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
          .Select(field => NormalizeRecordFieldName(field.Name))
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

        Assert.Equal(
          new[] { "ArgumentOrdinal", "PoolOrdinal" },
          fieldNames);

        // 端点与桥种类都【不得】回到载体里：它们由池内序号现取/导出。
        Assert.DoesNotContain("SourceNode", fieldNames);
        Assert.DoesNotContain("TargetNode", fieldNames);
        Assert.DoesNotContain("BridgeKind", fieldNames);
        Assert.DoesNotContain("CallSiteNode", fieldNames);
        Assert.DoesNotContain("TargetMethodNode", fieldNames);
        Assert.DoesNotContain("StableCallSiteOrder", fieldNames);
    }

    // 验收标准：计划元素宽度不超过旧宽度 60%。
    // ⑥ 把宽度压到 8 B（旧基线的 1.9%），该门槛仍然成立且余量极大。
    [Fact]
    public void InterproceduralPlanRef_Width_IsUnderSixtyPercentOfFrozenBaseline()
    {
        const int frozenPlanWidthBytes = 428;
        var planWidth = Unsafe.SizeOf<InterproceduralPlanRef>();

        Assert.True(
          planWidth * 100 <= frozenPlanWidthBytes * 60,
          $"计划宽度 {planWidth} B 必须不超过冻结基线 {frozenPlanWidthBytes} B 的 60%"
            + $"（当前 {planWidth * 100.0 / frozenPlanWidthBytes:F1}%）。");

        if (Environment.Is64BitProcess)
        {
            // 两个 int(4)，按 4 B 对齐 ⇒ 8 B。这是本项"延迟物化"的核心读数：
            // 端点不再内嵌，故 2 × NLCPGNode(104) 那 208 B 整体消失。
            Assert.Equal(8, planWidth);
            Assert.Equal(104, Unsafe.SizeOf<NLCPGNode>());
        }
    }

    // 组头承载调用点元数据，且自身仍是值类型（不引入逐组对象分配）。
    [Fact]
    public void InterproceduralDataFlowPlanGroup_OwnsCallSiteMetadataAsValueType()
    {
        Assert.True(typeof(InterproceduralDataFlowPlanGroup).IsValueType);

        var properties = typeof(InterproceduralDataFlowPlanGroup)
          .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
          .Select(property => property.Name)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

        // 两段式组头（跨调用点复用）：共享 ArgumentToParameter 前缀 + 私有尾段。
        // 单一 `Plans` 属性已被这两个属性取代 —— 组内下标改由 PlanAt 派发。
        Assert.Equal(
          new[]
          {
            "CallSiteNode", "Count", "GroupTailPlans", "SharedArgumentPlans",
            "StableCallSiteOrder",
          },
          properties);
    }

    // 计划载体必须继续是【非公开值类型】：NLCPGNodeIdContractTests 依赖它，
    // 且它一旦变成引用类型，"按值搬运"前提就整体失效。
    [Fact]
    public void InterproceduralPlanRef_StaysInternalValueType()
    {
        var type = typeof(InterproceduralPlanRef);

        Assert.True(type.IsValueType);
        Assert.False(type.IsPublic);
        Assert.False(type.IsNestedPublic);
    }

    // ── ② 正预算：前缀收集 ────────────────────────────────────────────────────────

    // 溢出组的【保留条数与容量】都不得超过 B。
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void PositiveBudget_OverflowingGroups_RetainNoMoreThanBudget(int budget)
    {
        var builder = Build(CreateOptions(budget), Source, "m1-budget-prefix.cs");
        var ledger = builder.LastInterproceduralPlanCapacity;

        Assert.True(ledger.PlanCountTotal > 0, "夹具没有产生任何计划，断言是空的。");
        Assert.True(
          ledger.MaxPlanCountPerGroup <= budget,
          $"最大单组保留 {ledger.MaxPlanCountPerGroup} 条 > 预算 {budget}。");
        Assert.True(
          ledger.MaxPlanCapacityPerGroup <= budget,
          $"最大单组容量 {ledger.MaxPlanCapacityPerGroup} > 预算 {budget}。");
        Assert.True(ledger.PlanCapacityTotal >= ledger.PlanCountTotal);
        Assert.True(ledger.PlanCapacitySlackBytes >= 0);
    }

    // ★ 本条是前缀收集的【判别性】断言 —— 它在旧实现上会失败。
    //
    // 为什么不能只断言最终容量：旧实现是「按上界全建 → RemoveRange → TrimExcess」，
    // TrimExcess 会把最终容量压回 B，所以【最终】容量与条数在旧代码上同样 ≤ B。
    // 只测最终值，新旧代码给出同一份账，那不是护栏而是装饰。
    //
    // 真正被消除的是【生成期曾按上界分配的那份数组】，它只体现在初始容量上：
    // 旧实现在小预算下初始容量 = 完整上界（可远大于 B），新实现 = min(上界, B)。
    // 本夹具的调用点命中多条 ArgumentToParameter 边，使上界明显大于预算。
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PositiveBudget_InitialCapacityNeverExceedsBudget(int budget)
    {
        var ledger = BuildLedgerForSource(Source, budget, "m1-initial-capacity.cs");

        Assert.True(
          ledger.PlanCountTotal > 0,
          "夹具未产生计划，本断言无判别力。");
        Assert.True(
          ledger.MaxPlanInitialCapacityPerGroup <= budget,
          $"某组初始容量 {ledger.MaxPlanInitialCapacityPerGroup} > 预算 {budget}；"
            + "前缀收集失效——仍在按未被截断的上界分配整份数组。");
    }

    // 对照：同一语料在大预算下的上界明显更大，证明上一条不是因为"上界本来就小"而成立。
    [Fact]
    public void SameSource_WithLargeBudget_HasLargerInitialCapacity()
    {
        var small = BuildLedgerForSource(Source, 1, "m1-initial-capacity.cs");
        var large = BuildLedgerForSource(Source, 10000, "m1-initial-capacity.cs");

        Assert.True(
          large.MaxPlanInitialCapacityPerGroup > small.MaxPlanInitialCapacityPerGroup,
          "若两种预算下初始容量相同，则说明上界从未大于预算，"
            + "上一条断言就是空的。实测："
            + $"small={small.MaxPlanInitialCapacityPerGroup}, large={large.MaxPlanInitialCapacityPerGroup}。");
    }

    // 预算越小，保留的计划总数必须单调不增——这条把"预算真的参与收集"证死。
    [Fact]
    public void SmallerBudget_RetainsMonotonicallyFewerOrEqualPlans()
    {
        var counts = new[] { 1, 2, 3, 10000 }
          .Select(budget => Build(CreateOptions(budget), Source, "m1-budget-monotone.cs")
            .LastInterproceduralPlanCapacity.PlanCountTotal)
          .ToArray();

        for (var index = 1; index < counts.Length; index += 1)
        {
            Assert.True(
              counts[index] >= counts[index - 1],
              $"预算递增时保留计划数必须单调不减，实测 {string.Join(", ", counts)}。");
        }
    }

    // 溢出时必须记一次原有的截断事件（名称与口径不变）。
    [Fact]
    public void BudgetOverflow_StillRecordsBoundaryEdgeBudgetCut()
    {
        var builder = Build(CreateOptions(1), Source, "m1-budget-cut.cs");

        Assert.True(
          builder.LastFlowSummaryMetrics.CutReasons.TryGetValue("BoundaryEdgeBudget", out var cuts)
            && cuts > 0,
          "前缀收集省略了超额计划，就必须补记 BoundaryEdgeBudget 截断事件。");
    }

    // ★ return-method 门控【不受预算影响】：溢出组的后续调用点不得重复产出
    //   ReturnToMethodReturn 桥。
    //
    // 这是 NLCPGBuilder.cs 中那条注释（约 1294 行）所承诺的语义：
    //   「即使本组已溢出，recordedReturnMethods 仍必须按原串行位置更新，
    //     否则后续调用点会重复产出 ReturnToMethodReturn 桥。」
    //
    // 判别方式：把同一语料在「预算充足」与「预算极小」两种情况下各构一次图，
    // 统计 ReturnToMethodReturn 桥的数量。前缀收集只允许【减少】计划，
    // 绝不允许让门控失效而【增加】桥。若把 recordedReturnMethods.Add
    // 挪进预算检查之内（即溢出时跳过更新），本断言会失败。
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BudgetOverflow_DoesNotDuplicateReturnToMethodReturnBridges(int budget)
    {
        const string filePath = "m1-return-gate.cs";
        var source = CpgBuilderSources.InterproceduralPlanRowThresholdPressure(3, 6);

        var full = CountReturnBridges(
          new NLCPGBuilder(CreateOptions(100_000)).BuildFromSource(source, filePath));
        var cut = CountReturnBridges(
          new NLCPGBuilder(CreateOptions(budget)).BuildFromSource(source, filePath));

        Assert.True(
          full > 0,
          "夹具未产出任何 ReturnToMethodReturn 桥，本断言无判别力。");
        Assert.True(
          cut <= full,
          $"预算 {budget} 下 ReturnToMethodReturn 桥为 {cut}，多于充足预算下的 {full}；"
            + "说明溢出后 return-method 门控被跳过，后续调用点重复产出了桥。");
    }

    private static int CountReturnBridges(NLCPGGraph graph)
    {
        return graph.Edges.Count(
          edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow
            && edge.StructuredLabel?.InterproceduralBridgeKind
              == NLCPGInterproceduralBridgeKind.ReturnToMethodReturn);
    }

    // 未溢出但上界偏大时，slack 必须进入账本；否则"取消无条件 TrimExcess"是无据承诺。
    [Fact]
    public void LargeUpperBoundWithoutOverflow_RecordsCapacitySlack()
    {
        var builder = Build(CreateOptions(10000), Source, "m1-budget-slack.cs");
        var ledger = builder.LastInterproceduralPlanCapacity;

        Assert.False(
          builder.LastFlowSummaryMetrics.CutReasons.ContainsKey("BoundaryEdgeBudget"),
          "本夹具在预算 10000 下不应发生截断，否则这条断言测的不是 slack。");
        Assert.True(ledger.PlanCountTotal > 0);
        Assert.True(
          ledger.PlanCapacityTotal >= ledger.PlanCountTotal,
          "容量不可能小于元素数。");
    }

    // ── ③ 非正预算：冻结既有异常，不得静默变成"跳过" ──────────────────────────────

    // 改前实测（probe.txt）：
    //   int.MinValue / -1 → ArgumentOutOfRangeException（RemoveRange 取负长度）
    //   0                 → ArgumentOutOfRangeException（发布器读 plans[0]）
    // 方案 B 移除了 plans[0] 这次读取，故必须显式复刻，否则 0 会从"失败"变成
    // "成功且少发边"——那是一处静默的契约变更。
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonPositiveBudget_KeepsFrozenOutOfRangeContract(int budget)
    {
        var builder = new NLCPGBuilder(CreateOptions(budget));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
          () => builder.BuildFromSource(Source, "m1-budget-nonpositive.cs"));

        Assert.Equal("index", exception.ParamName);
    }

    // 对照组：预算 0 与 1 不得等价（否则上面那条"抛出"可以靠"其实都一样"蒙混）。
    [Fact]
    public void BudgetZeroAndOne_AreNotEquivalent()
    {
        var oneGraph = new NLCPGBuilder(CreateOptions(1))
          .BuildFromSource(Source, "m1-budget-distinct.cs");

        Assert.True(
          oneGraph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow) > 0);

        var zeroBuilder = new NLCPGBuilder(CreateOptions(0));
        Assert.Throws<ArgumentOutOfRangeException>(
          () => zeroBuilder.BuildFromSource(Source, "m1-budget-distinct.cs"));
    }

    // ── ⑤ 跨调用点复用（c²·p → c·p）的定向护栏（执行文档 §5.3，N1–N5）────────────────
    //
    // 共同前提（§2.5 的边界，必须先讲清，否则会写出没有判别力的断言）：
    // 复用【只】收缩同时驻留的载体份数 —— 计划条数、窗口行数、边数与 List.Sort 比较次数
    // 全部【不变】。故收益的唯一判别量是账本里的载体【份数】，
    // 绝不能用 PeakWindowRows 或边数当判据（它们按设计不动）。

    // N1：共享前缀把 c²·p 的载体份数降到 c·p 量级。
    // 夹具：单个 int 形参的 Leaf 被 c 个调用点各调一次 ⇒ 桶大小 = c（p = 1）。
    // 改造前每个调用点都重放整桶 ⇒ 段 1 载体合计 = c·c；改造后共享一份 ⇒ ≈ c + O(c)。
    [Theory]
    [InlineData(70)]
    [InlineData(90)]
    public void SharedArgumentSegment_RetainsFarFewerCarriersThanPerGroupReplay(int callCount)
    {
        var ledger = BuildLedger(callCount, dop: 1);

        // p == 1 ⇒ 每方法实参桶 = 调用点数。
        var perGroupReplayCarriers = (long)callCount * callCount;

        Assert.True(
          ledger.PlanCountTotal >= callCount,
          $"保留载体仅 {ledger.PlanCountTotal} 份，少于共享段本身应有的 {callCount} 份，"
            + "共享前缀根本没建起来。");
        Assert.True(
          ledger.PlanCountTotal < perGroupReplayCarriers,
          $"保留载体 {ledger.PlanCountTotal} 份未低于逐调用点重放的 {perGroupReplayCarriers} 份；"
            + "共享前缀未生效——每个调用点仍在重放整桶。");
    }

    // N2：共享前缀必须【懒惰】——只为真正被调用的目标方法构造，而不是循环前为所有方法预建。
    //
    // 判别力来自夹具里【有方法从未被调用】：OrderSample 声明 5 个方法
    // （Leaf/Middle/Top/Wide/ReturnHeavy），但只有 Leaf 与 Middle 被调用。
    // 若实现改成"循环前无条件为所有方法键预建"，本计数会是 5；懒惰实现必须是 2。
    // 这条同时护住 §3.3 的另一半：c == 1 的方法占多数，为它们预建是纯亏。
    [Fact]
    public void SharedArgumentSegment_IsBuiltLazilyOnlyForDemandedMethods()
    {
        var ledger = BuildLedgerForSource(Source, 10000, "m1-lazy-construction.cs");

        // 夹具中真正被调用的目标方法：Leaf、Middle。
        Assert.Equal(2, ledger.SharedArgumentMethodCount);
        Assert.True(
          ledger.SharedArgumentMethodCount < 5,
          $"共享段方法数为 {ledger.SharedArgumentMethodCount}，等于夹具声明的 5 个方法，"
            + "说明前缀是为所有方法预建的，而不是按需构造的。");
    }

    // N3：共享段必须【按方法计一次】——这是 §4.4.2 重复计数陷阱的守门断言。
    // 若把同一份共享 buffer 逐组各累加一次，本值会变成 c²（= 8100）而不是 c（= 90），
    // 于是本项的收益在账本上与改造前【无差别】，完全不可观测。
    [Fact]
    public void SharedArgumentSegmentLedger_CountsEachMethodExactlyOnce()
    {
        const int callCount = 90;
        var ledger = BuildLedger(callCount, dop: 1);

        Assert.Equal(callCount, ledger.SharedArgumentPlanCountTotal);
        Assert.Equal(1, ledger.SharedArgumentMethodCount);
        Assert.True(
          ledger.SharedArgumentPlanCountTotal < (long)callCount * callCount,
          "共享段条数达到 c²，说明它被逐组重复累加了。");
    }

    // N4：共享前缀缓存必须【逐文档】。它保存的是本图【池内序号】；若跨文档泄漏，
    // 序号在另一张图里依然"合法"却指向别的边 ⇒ 静默错边，且不抛任何异常
    // （这是本项最严重的错误类型）。判别方式：同一份源码分别用 BuildMany（两文件同批次）
    // 与 BuildFromSource（各自单独）构建，逐文件比较跨过程桥描述串。
    //
    // 两个文件必须是【不同类型名】：D1 多文件走的是同一份 compilation，
    // 同名类型会构成重复定义而让语义模型出错——那是夹具的问题，不是本项要测的行为。
    // 类型名不同也正好让"两份内容真正不同"，从而任何跨文档串号都会改变描述串。
    [Fact]
    public void SharedArgumentPrefixCache_DoesNotLeakAcrossDocuments()
    {
        var sourceA = CpgBuilderSources.InterproceduralPlanCapacityPressure(9)
          .Replace("CapacityPressure", "DocAlpha", StringComparison.Ordinal)
          .Replace("Leaf", "LeafAlpha", StringComparison.Ordinal);
        var sourceB = CpgBuilderSources.InterproceduralPlanCapacityPressure(9)
          .Replace("CapacityPressure", "DocBeta", StringComparison.Ordinal)
          .Replace("Leaf", "LeafBeta", StringComparison.Ordinal);
        const string firstPath = "m1-doc-scope-a.cs";
        const string secondPath = "m1-doc-scope-b.cs";

        var batched = new NLCPGBuilder(CreateOptions(10000))
          .BuildMany(new[] { (firstPath, sourceA), (secondPath, sourceB) });

        // 单文件对照组必须与批次内的同名图逐边一致。
        var singleA = new NLCPGBuilder(CreateOptions(10000))
          .BuildFromSource(sourceA, firstPath);
        var singleB = new NLCPGBuilder(CreateOptions(10000))
          .BuildFromSource(sourceB, secondPath);

        var batchedA = DescribeBridges(batched[firstPath]);
        var batchedB = DescribeBridges(batched[secondPath]);
        Assert.NotEmpty(batchedA);
        Assert.NotEmpty(batchedB);

        Assert.Equal(DescribeBridges(singleA), batchedA);
        Assert.Equal(DescribeBridges(singleB), batchedB);
    }

    // N5：组的条数/派发必须按【两段之和】成立，而不是只看尾段。
    //
    // 这是两段式引入的、唯一会【静默丢边】的缺陷：某个组完全可能"共享段非空、尾段为空"
    // （被调方没有返回类桥，而实参桶非空）。若构造段把判空写成
    // `groupTailPlans.Count == 0`，这些组会被当成空组 `continue` 跳过，于是一条边都不发，
    // 且【不抛任何异常】—— 边数、冻结哈希与 NodeId 分配都会静默改变。
    //
    // 判别方式：直接构造"共享段非空 / 尾段为空"的组头，断言
    //   · Count 反映两段之和（不是尾段的 0）；
    //   · PlanAt 在统一编号空间里把 [0, sharedCount) 派发给共享段、其余派发给尾段。
    // 直接测组头是因为构造段的判空读的就是 Count —— 这样断言与缺陷模式一一对应，
    // 不依赖某个源码语料恰好产出空尾段的组（实测 void 被调方仍会产出返回类桥）。
    [Fact]
    public void GroupWithNonEmptySharedSegmentAndEmptyTail_CountsAndDispatchesBothSegments()
    {
        var shared = InterproceduralPlanBuffer.Create(2);
        shared.Add(new InterproceduralPlanRef(PoolOrdinal: 11));
        shared.Add(new InterproceduralPlanRef(PoolOrdinal: 22, ArgumentOrdinal: 1));
        var emptyTail = InterproceduralPlanBuffer.Create(0);

        var group = new InterproceduralDataFlowPlanGroup(
          default,
          stableCallSiteOrder: 0,
          shared,
          emptyTail);

        // 尾段为空【不】意味着组为空 —— 判空必须看两段之和。
        Assert.Equal(0, group.GroupTailPlans.Count);
        Assert.Equal(2, group.Count);

        // 统一编号空间：共享段在前，尾段随后。
        Assert.Equal(11, group.PlanAt(0).PoolOrdinal);
        Assert.Equal(22, group.PlanAt(1).PoolOrdinal);
        Assert.Equal(1, group.PlanAt(1).ArgumentOrdinal);

        // 反向形态：共享段为空 / 尾段非空。两段的编号必须连续，否则 List.Sort 的
        // 末键（PlanIndex）就不再是全序，边序会随排序实现漂移（§8 第 5 条）。
        var tail = InterproceduralPlanBuffer.Create(1);
        tail.Add(new InterproceduralPlanRef(PoolOrdinal: 33));
        var tailOnlyGroup = new InterproceduralDataFlowPlanGroup(
          default,
          stableCallSiteOrder: 0,
          InterproceduralPlanBuffer.Empty,
          tail);

        Assert.Equal(1, tailOnlyGroup.Count);
        Assert.Equal(33, tailOnlyGroup.PlanAt(0).PoolOrdinal);
    }

    // ── ④ 窗口边界与空闲槽容量治理 ────────────────────────────────────────────────

    // 夹具必须真的跨窗口：90 个调用点 > 64 组上界 ⇒ 至少两次 flush。
    // 若这条不成立，"窗口容量治理"就从未被执行过，后面的断言全是空测试。
    [Theory]
    [InlineData(70)]
    [InlineData(90)]
    public void MoreThanOneWindowOfGroups_CrossesWindowBoundary(int callCount)
    {
        var builder = Build(
          CreateOptions(1),
          CpgBuilderSources.InterproceduralPlanCapacityPressure(callCount),
          "m1-window-crossing.cs");
        var ledger = builder.LastInterproceduralPlanCapacity;

        Assert.True(
          ledger.FlushCount >= 2,
          $"{callCount} 个调用点应至少跨一次 64 组窗口边界，实测 flush={ledger.FlushCount}。");
        Assert.True(ledger.PeakWindowGroups > 0 && ledger.PeakWindowGroups <= 64);
    }

    // 窗口的【行】上界（131072）必须真的被执行过，而不是只撑满【组】上界（64）。
    //
    // 夹具形态由实测定律决定：组行数 ≈ 形参个数 × 指向该方法的调用点数。
    // 因此【形参 × 调用点】才能低成本越过行阈值；只靠加形参是超线性昂贵且不可行的
    // （实测 params=5000 约 84 s、params=20000 约 512 s）。
    //
    // 关键判据是 peakGroups < 64：若冲刷是由 64 组上界触发的，组数必然等于 64。
    // 组数小于 64 而 peakRows 越界，才【唯一地】证明是行上界先触发。
    [Fact]
    public void WindowRowBound_FiresBeforeGroupBound_WhenOneGroupIsWide()
    {
        var builder = Build(
          CreateOptions(100_000),
          CpgBuilderSources.InterproceduralPlanRowThresholdPressure(40, 64),
          "m1-row-threshold.cs");
        var ledger = builder.LastInterproceduralPlanCapacity;

        Assert.True(
          ledger.PeakWindowRows > WindowMaxRowsProbe,
          $"夹具必须先真的越过行上界 {WindowMaxRowsProbe}，实测峰值 {ledger.PeakWindowRows} 行；"
            + "否则这条测试没有测到行上界。");
        Assert.True(
          ledger.PeakWindowGroups < WindowMaxGroupsProbe,
          $"峰值组数 {ledger.PeakWindowGroups} 应小于组上界 {WindowMaxGroupsProbe}，"
            + "否则无法区分冲刷是由行上界还是组上界触发。");
        Assert.True(
          ledger.FlushCount >= 2,
          $"越界后应至少冲刷两次，实测 flush={ledger.FlushCount}。");
        Assert.True(ledger.MaxPlanCountPerGroup > 0);
    }

    // 对照组：同样的调用点数、更少的形参 ⇒ 不得越界。
    // 没有这条，"上面那条越界"可能只是调用点数的功劳，与组宽无关。
    [Fact]
    public void WindowRowBound_DoesNotFire_WhenGroupsAreNarrow()
    {
        var builder = Build(
          CreateOptions(100_000),
          CpgBuilderSources.InterproceduralPlanRowThresholdPressure(20, 64),
          "m1-row-threshold-control.cs");
        var ledger = builder.LastInterproceduralPlanCapacity;

        Assert.True(
          ledger.PeakWindowRows <= WindowMaxRowsProbe,
          $"窄组夹具不应越过行上界，实测峰值 {ledger.PeakWindowRows} 行。");
        Assert.True(ledger.FlushCount >= 1);
    }

    // 行上界路径触发后，空闲槽治理仍须成立：淘汰后保留量不得越预算。
    [Fact]
    public void WindowRowBound_PostFlushRetainedCapacity_StaysWithinBudget()
    {
        var builder = Build(
          CreateOptions(100_000),
          CpgBuilderSources.InterproceduralPlanRowThresholdPressure(40, 64),
          "m1-row-threshold-budget.cs");
        var ledger = builder.LastInterproceduralPlanCapacity;

        var budgetBytes = (long)WindowMaxRowsProbe * PlanSortRowWidth();
        Assert.True(
          ledger.SortBufferRetainedBytes <= budgetBytes,
          $"行上界冲刷后保留 {ledger.SortBufferRetainedBytes} B，超出内部预算 {budgetBytes} B。");
        Assert.True(
          ledger.SortBufferPeakRetainedBytes > ledger.SortBufferRetainedBytes,
          "该夹具在冲刷前应确实超出预算，否则淘汰从未发生，这条断言是空的。");
        Assert.True(ledger.SortBufferReclaimedSlots >= 1);
    }

    // 空闲排序缓冲的【合计】保留量必须落在内部预算内，且淘汰是确定性的：
    // 同一输入重复构建必须给出逐字段相同的账本。
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SortBufferRetention_StaysWithinInternalBudgetAndIsDeterministic(int dop)
    {
        var first = BuildLedger(90, dop);
        var second = BuildLedger(90, dop);

        Assert.Equal(first, second);

        var budgetBytes = (long)WindowMaxRowsProbe * PlanSortRowWidth();
        Assert.True(
          first.SortBufferRetainedBytes <= budgetBytes,
          $"空闲槽合计保留 {first.SortBufferRetainedBytes} B 超出内部预算 {budgetBytes} B。");
        Assert.True(first.SortBufferRetainedBytes >= 0);

        // 治理只会降低保留量，绝不会增加。
        Assert.True(first.SortBufferRetainedBytes <= first.SortBufferPeakRetainedBytes);
    }

    // 淘汰决策的【纯函数】直测。
    //
    // 为什么必须直测而不是靠语料：窗口在 131072 行处就冲刷，所以"当前窗口各槽容量之和"
    // 几乎总在预算之内，用真实输入很难稳定触发回收。若不直测这个决策函数，
    // "确定性淘汰"实际上没有任何测试覆盖，删掉整段回收逻辑也不会有测试失败。
    [Fact]
    public void SelectSortSlotsToReclaim_WithinBudget_ReclaimsNothing()
    {
        var capacities = new[] { 100, 200, 300 };
        var reclaimed = NLCPGBuilder.SelectSortSlotsToReclaim(
          capacities,
          rowWidth: 32,
          retainedBudgetBytes: 600L * 32);

        Assert.Empty(reclaimed);
    }

    [Fact]
    public void SelectSortSlotsToReclaim_OverBudget_EvictsLargestFirstAndStopsAtBudget()
    {
        // 总保留 = (1000+10+10) * 32 = 32640 B；预算 = 500 * 32 = 16000 B。
        // 只淘汰最大的槽 0 之后 = (10+10)*32 = 640 B ≤ 预算 ⇒ 必须立刻停止，
        // 不得顺手把其余槽也清掉（那会白白丢弃可复用的容量）。
        var capacities = new[] { 1000, 10, 10 };
        var reclaimed = NLCPGBuilder.SelectSortSlotsToReclaim(
          capacities,
          rowWidth: 32,
          retainedBudgetBytes: 500L * 32);

        Assert.Equal(new[] { 0 }, reclaimed);
    }

    // 同容量时必须按槽号升序淘汰，否则"确定性"依赖 LINQ 的实现细节。
    [Fact]
    public void SelectSortSlotsToReclaim_TiedCapacities_EvictsByAscendingSlot()
    {
        var capacities = new[] { 500, 500, 500, 500 };
        var reclaimed = NLCPGBuilder.SelectSortSlotsToReclaim(
          capacities,
          rowWidth: 32,
          retainedBudgetBytes: 1000L * 32);

        Assert.Equal(new[] { 0, 1 }, reclaimed);
    }

    [Fact]
    public void SelectSortSlotsToReclaim_ZeroCapacitySlots_AreNeverEvicted()
    {
        var capacities = new[] { 0, 900, 0 };
        var reclaimed = NLCPGBuilder.SelectSortSlotsToReclaim(
          capacities,
          rowWidth: 32,
          retainedBudgetBytes: 100L * 32);

        Assert.Equal(new[] { 1 }, reclaimed);
    }

    // 淘汰后保留量必须真的落到预算内——这是该函数存在的全部意义。
    [Fact]
    public void SelectSortSlotsToReclaim_ResultSatisfiesBudget()
    {
        const long rowWidth = 32;
        const long budget = 777L * rowWidth;
        var capacities = new[] { 5000, 1, 2000, 7, 900, 400 };
        var reclaimed = NLCPGBuilder.SelectSortSlotsToReclaim(capacities, rowWidth, budget);

        var remaining = capacities.Sum(capacity => (long)capacity * rowWidth);
        foreach (var slot in reclaimed)
        {
            remaining -= capacities[slot] * rowWidth;
        }

        Assert.True(
          remaining <= budget,
          $"淘汰后仍保留 {remaining} B > 预算 {budget} B。");
    }

    // 已发布槽的行载荷必须被清空，且回收必然以清空为前提。
    //
    // ✅ 判别力已修复（2026-09-25 第四会话）：本条原先只断言账本计数器
    // `SortBufferClearedSlots`，而产品代码里该计数器紧跟在 `rows.Clear();` 之后递增
    // ⇒ **把 `rows.Clear();` 整行删掉、只留计数器，本条测试照样通过**（变异实测 39/39 通过）。
    // 那锁的是"计数器被加过"，对总索引 §4 的「强引用可达性」轴没有判别力。
    //
    // 现产品侧在 `Clear()` 之后**实测 `rows.Count`** 并累加为 `SortBufferResidualRows`
    // ⇒ 下面第 3 条断言直接锁"清空之后槽里到底还剩几行"这一可观测事实：
    // 删掉 `rows.Clear();` 会让该值 > 0 并当场失败。
    // 之所以必须在产品侧记账：窗口槽 `windowRows` 是 `FlushParallelPublishWindow` 的形参，
    // 构建结束后测试无法从任何 API 观察槽内容（这是原复验者无法在测试侧补断言的原因）。
    //
    // ⚠️ 【C 排序键排名化】改变了本条的立论轴，但【没有】使它失效：
    // 改前行持 `SourceKey`/`TargetKey` 两个字符串引用，清空释放的是这两个强引用；
    // 改后行只含 int（无字符串字段，见 CpgInterproceduralEdgeOrderTests 的字段类型断言）
    // ⇒ 「字符串强引用可达性」这根轴已【不存在】，不再由本条承担。
    // 本条保留断言并继续有效的理由是另一根轴：`List<PlanSortRow>` 的**容量数组**仍会存活
    // （Clear() 不缩容），残留行数 > 0 意味着"已发布槽仍持有行数据"，语义上依旧是错的。
    // ⇒ 断言保持不变，但其含义从"释放字符串引用"变为"槽确实被清空"。
    // 这是本测试前提的第二次收窄，如实记录以免后续读者以为判据没变。
    [Fact]
    public void PublishedSlots_ReleaseSortRowKeyReferences()
    {
        var ledger = BuildLedger(90, dop: 1);

        Assert.True(
          ledger.SortBufferClearedSlots > 0,
          "没有任何槽被清空 ⇒ 已发布槽仍持有排序行数据。");
        Assert.True(ledger.SortBufferClearedSlots >= ledger.FlushCount);
        Assert.True(
          ledger.SortBufferReclaimedSlots <= ledger.SortBufferClearedSlots,
          "被回收的槽数不可能多于被清空的槽数。");
    }

    // ★ 强引用可达性的【真实可观测】判据：清空之后残留行数必须为 0。
    //
    // 与上一条的分工：上一条锁"清空动作发生过"（计数器），本条锁"清空**真的生效**"
    // （Clear() 之后实测的 rows.Count 合计）。二者不是重复：
    // 删除 `rows.Clear();` 时上一条仍然通过，只有本条会失败。
    [Fact]
    public void PublishedSlots_RetainNoResidualSortRowsAfterClear()
    {
        var ledger = BuildLedger(90, dop: 1);

        Assert.True(
          ledger.SortBufferClearedSlots > 0,
          "没有任何槽进入清空路径 ⇒ 本判据的夹具没有判别力。");
        Assert.Equal(0, ledger.SortBufferResidualRows);
    }

    // ★ 淘汰【接线】的直测：把"合计保留 ≤ 预算"从注释变成可观测事实。
    //
    // 为什么不能用真实语料：窗口在 131072 行就冲刷，所以各槽合计容量通常都在预算内，
    // 真实构建**几乎不会**触发回收。若只靠语料，这条路径等于没有测试覆盖，
    // 整段回收被删掉也不会有测试失败。这里直接给合成槽数组，驱动真实的接线。
    [Fact]
    public void ReclaimIdleSortBufferCapacity_ActuallyReplacesOverBudgetSlots()
    {
        const int slotCount = 4;
        // 预算按【行数】计（131072 × 行宽），而槽的容量是【跨窗口保留】的：
        // 一个窗口最多攒到约 131072 行，但每个槽会长期留住它历史上遇到过的最大组的容量。
        // 于是当语料里有一批大组、把它们分散到多个槽之后，64 个槽的合计保留量
        // 可以远超任一时刻的真实需求 —— 这正是本治理要处理的情形。
        // 夹具据此取一个必然越界的合计容量。
        const int bigCapacity = 200_000;
        var rows = new List<NLCPGBuilder.PlanSortRow>[slotCount];
        rows[0] = new List<NLCPGBuilder.PlanSortRow>(bigCapacity);
        rows[1] = new List<NLCPGBuilder.PlanSortRow>(10);
        rows[2] = new List<NLCPGBuilder.PlanSortRow>(10);
        rows[3] = new List<NLCPGBuilder.PlanSortRow>(10);

        var rowWidth = PlanSortRowWidth();
        var ledger = new InterproceduralPlanCapacityLedgerBuilder();
        var before = (long)(bigCapacity + 30) * rowWidth;
        var budget = (long)WindowMaxRowsProbe * rowWidth;
        Assert.True(before > budget, "夹具必须先真的超出预算，否则这条测试是空的。");

        NLCPGBuilder.ReclaimIdleSortBufferCapacity(rows, ledger);
        var result = ledger.ToLedger();

        // 1. 峰值必须记录【治理之前】的字节，且严格大于治理之后 —— 这是"确实发生了治理"的证据。
        Assert.True(
          result.SortBufferPeakRetainedBytes >= before,
          $"峰值 {result.SortBufferPeakRetainedBytes} 必须 ≥ 治理前 {before}。");
        Assert.True(
          result.SortBufferPeakRetainedBytes > result.SortBufferRetainedBytes,
          "峰值与保留值相等 ⇒ 本次没有淘汰任何槽，本测试未覆盖目标路径。");

        // 2. 超大槽必须被真的替换（不是只改了账本数字）。
        Assert.True(result.SortBufferReclaimedSlots >= 1);
        Assert.Equal(0, rows[0].Capacity);
        Assert.Equal(0, rows[0].Count);

        // 3. 治理之后确实落回预算内。
        Assert.True(result.SortBufferRetainedBytes <= budget);
    }

    // 预算之内时【不得】淘汰任何槽：容量留着复用才是本设计的目的，
    // 无条件清空会把"取消 TrimExcess"的收益又还回去。
    [Fact]
    public void ReclaimIdleSortBufferCapacity_WithinBudget_KeepsAllSlots()
    {
        var rows = new List<NLCPGBuilder.PlanSortRow>[3];
        rows[0] = new List<NLCPGBuilder.PlanSortRow>(8);
        rows[1] = new List<NLCPGBuilder.PlanSortRow>(4);
        rows[2] = new List<NLCPGBuilder.PlanSortRow>(2);

        var ledger = new InterproceduralPlanCapacityLedgerBuilder();
        NLCPGBuilder.ReclaimIdleSortBufferCapacity(rows, ledger);
        var result = ledger.ToLedger();

        Assert.Equal(0, result.SortBufferReclaimedSlots);
        Assert.Equal(8, rows[0].Capacity);
        Assert.Equal(4, rows[1].Capacity);
        Assert.Equal(2, rows[2].Capacity);
        Assert.Equal(
          (long)(8 + 4 + 2) * PlanSortRowWidth(),
          result.SortBufferRetainedBytes);
    }

    // ★ 轮流大槽压力（执行文档第 7 节验收明确要求："含轮流大槽压力测试"）。
    //
    // 真实触发形态不是"单个窗口内部超预算"——窗口在 131072 行就冲刷，窗口内根本攒不到。
    // 真正的触发是【跨窗口累积】：每个槽会把它历史上遇到过的最大组的容量长期留住，
    // 而大组是轮流落到不同槽的（调用点顺序决定），于是各槽合计保留量随窗口数上升，
    // 直到越过预算才被淘汰。本测试按这一序列逐窗口推进，并在【每次】刷新后校验不变量。
    [Fact]
    public void AlternatingLargeGroups_AcrossWindows_KeepRetainedCapacityWithinBudget()
    {
        const int slotCount = 64;
        // 单组 20000 行 ⇒ 该槽经 List 倍增后 Capacity = 32768 行。
        // 4 个这样的槽 = 131072 行，恰好等于预算；第 5 个必然越界 ⇒ 淘汰必须在此刻发生。
        // 容量由 List 自身倍增决定，**不由测试指定**，否则就变成在断言自己写下的数字。
        const int largeGroupRows = 20_000;
        const int windowCount = 8;

        var rowWidth = PlanSortRowWidth();
        var budgetBytes = (long)WindowMaxRowsProbe * rowWidth;
        Assert.True(budgetBytes > 0);

        var rows = new List<NLCPGBuilder.PlanSortRow>[slotCount];
        for (var slot = 0; slot < slotCount; slot += 1)
        {
            rows[slot] = new List<NLCPGBuilder.PlanSortRow>();
        }

        var ledger = new InterproceduralPlanCapacityLedgerBuilder();
        var evictedAtWindow = -1;

        for (var window = 0; window < windowCount; window += 1)
        {
            // 本窗口的大组落到这个槽。Add 到 20000 行再 Clear —— 复刻发布路径：
            // 元素引用被清掉，但容量被留住（这正是"空闲槽"的定义）。
            var slot = window;
            for (var row = 0; row < largeGroupRows; row += 1)
            {
                rows[slot].Add(default);
            }

            rows[slot].Clear();
            Assert.Equal(0, rows[slot].Count);

            var reclaimedBefore = ledger.SortBufferReclaimedSlots;
            NLCPGBuilder.ReclaimIdleSortBufferCapacity(rows, ledger);
            var result = ledger.ToLedger();

            // 不变量①：每次刷新之后，合计保留都必须落在内部预算内。
            // 若回收逻辑被删掉，合计保留会随窗口数单调上涨并在这里失败。
            Assert.True(
              result.SortBufferRetainedBytes <= budgetBytes,
              $"第 {window} 个窗口后合计保留 {result.SortBufferRetainedBytes} B 超出预算 {budgetBytes} B。");

            // 不变量②：已发布槽的 key 引用必须已清空——清空是回收的前提。
            Assert.Equal(0, rows[slot].Count);

            if (ledger.SortBufferReclaimedSlots > reclaimedBefore)
            {
                evictedAtWindow = window;
            }
        }

        // 这两条把"本测试确实压到了目标路径"钉死：
        //   · 5 个 32768 行的槽合计 163840 行 > 131072 行预算 ⇒ 必须观察到淘汰；
        //   · 治理前的峰值必须真的高于预算，否则这条压力测试是空的。
        Assert.True(
          evictedAtWindow >= 0,
          $"{windowCount} 个大组轮流落到不同槽后合计必然超过 {WindowMaxRowsProbe} 行预算，必须至少观察到一次淘汰。");
        Assert.True(
          ledger.SortBufferPeakRetainedBytes > budgetBytes,
          $"治理前峰值 {ledger.SortBufferPeakRetainedBytes} B 未超过预算 {budgetBytes} B ⇒ 本夹具压力不足。");
        Assert.True(ledger.SortBufferReclaimedSlots >= 1);
    }

    // 淘汰之后，被淘汰槽必须是【可复用】的空列表，而不是 null 或带残留元素的列表：
    // 下一批大组还要往这些槽里写，留下 null 会在并行发布时炸掉。
    [Fact]
    public void ReclaimedSlots_RemainReusableEmptyLists()
    {
        var rows = new List<NLCPGBuilder.PlanSortRow>[3];
        for (var slot = 0; slot < rows.Length; slot += 1)
        {
            rows[slot] = new List<NLCPGBuilder.PlanSortRow>();
        }

        for (var row = 0; row < 200_000; row += 1)
        {
            rows[0].Add(default);
        }

        rows[0].Clear();
        var ledger = new InterproceduralPlanCapacityLedgerBuilder();
        NLCPGBuilder.ReclaimIdleSortBufferCapacity(rows, ledger);

        Assert.True(ledger.SortBufferReclaimedSlots >= 1);
        foreach (var slot in rows)
        {
            Assert.NotNull(slot);
            Assert.Empty(slot);
            // 可继续写入（模拟下一个窗口向该槽追加行）。
            slot.Add(default);
            Assert.Single(slot);
        }
    }

    // ★ 受控窗口峰值账本（执行文档第 7 节最后一条验收）：
    //   "受控窗口峰值账本含组头、slack、旧/新数组共存，必须有净改善且满足共同耗时门槛。"
    //
    // 口径声明（避免重犯设计文档第 8 节的错误）：
    //   · 峰值取【窗口内同时存活的行数】= 实测 PeakWindowRows，**不是**累计 PlanCountTotal。
    //     用累计 P×428 当收益会把"整轮构建总量"冒充"同时存活峰值"，那是错的。
    //   · 本账把新实现的【组头数组】与【实测 slack】都当作成本计入，不白送。
    //   · 这里只算【方案 B 的布局账】。空闲槽容量治理是另一个子补丁，按 Task 2.4
    //     "这一步不把容量治理收益混入布局收益"，故两侧都按同一份排序缓冲计，互相抵消。
    //   · 这是【由实测宽度与实测峰值推出的受控账】，**不是**进程内存实测值；
    //     耗时是另一条独立门槛，见 ControlledWindowPeakLedger_... 的说明与 BASELINE.md。
    [Theory]
    [InlineData(90)]
    [InlineData(150)]
    public void ControlledWindowPeakLedger_ChargesGroupHeaderAndSlack_StillShowsNetImprovement(
      int callCount)
    {
        var ledger = BuildLedger(callCount, dop: 1);

        const int frozenOldPlanWidthBytes = 428;
        var newPlanWidthBytes = Unsafe.SizeOf<InterproceduralPlanRef>();
        var groupHeaderBytes = Unsafe.SizeOf<InterproceduralDataFlowPlanGroup>();
        const int windowSlots = 64;

        Assert.True(
          ledger.PeakWindowRows > 0,
          "夹具未观测到窗口峰值行数，本账会是空的。");
        Assert.True(
          ledger.PlanCountTotal > 0,
          "夹具未产生计划，本账会是空的。");

        // 新实现保留了 slack（取消了无条件 TrimExcess），故按实测的容量/条数比
        // 在【峰值】上把 slack 也计费。不这么做就等于让新实现在账面上白拿一份容量。
        var capacityOverCount = (double)ledger.PlanCapacityTotal / ledger.PlanCountTotal;
        Assert.True(capacityOverCount >= 1.0, "容量不得小于条数。");

        var peakRows = ledger.PeakWindowRows;
        var chargedNewPlanRows = (long)Math.Ceiling(peakRows * capacityOverCount);

        // 旧：计划载荷 = 428 B × 峰值行；窗口是 64 个 List 引用。
        var oldPeakBytes =
          (long)frozenOldPlanWidthBytes * peakRows
          + ((long)windowSlots * IntPtr.Size);
        // 新：计划载荷 = 8 B ×（峰值行 × 实测 slack 比）；窗口是 64 个组头【值】。
        // ⑥ 后载体只剩两个 int，端点与桥种类都改为按池内序号现取（池本身是 M2 的既有常驻事实，
        // 不因本项增减），故这里的每行载荷如实按 8 B 计。slack 比为 1.0——精确分配无桶对齐浪费。
        var newPeakBytes =
          (long)newPlanWidthBytes * chargedNewPlanRows
          + ((long)windowSlots * groupHeaderBytes);

        // 【旧/新数组共存】——第 3 节明确要求计入扩容瞬间的双份数组，不能按"稳态"记账。
        // 发生点是 BuildAndSortPlanRows 里的 `sortRows.Capacity = callSitePlans.Count`：
        // 该槽的排序数组由小变大时，旧数组尚未被回收、新数组已经分配，两片同时存活。
        // 上界取【最大单组】而非整个窗口：扩容是逐槽发生的，同一时刻只有一个槽在长。
        // 两侧都经历同一过程（排序行布局未变），故它对旧/新相同、在比较中抵消；
        // 显式写出是为了表明它被【计入过】而不是被漏掉。
        var sortRowWidth = PlanSortRowWidth();
        var resizeTransientBytes = (long)sortRowWidth * ledger.MaxPlanCountPerGroup;
        Assert.True(resizeTransientBytes > 0, "瞬态项必须为正；否则说明本项未被真正计入。");

        var oldPeakWithTransient = oldPeakBytes + resizeTransientBytes;
        var newPeakWithTransient = newPeakBytes + resizeTransientBytes;
        Assert.True(
          oldPeakWithTransient > newPeakWithTransient,
          $"计入扩容瞬态后未见净改善：旧 {oldPeakWithTransient} B vs 新 {newPeakWithTransient} B。");

        Assert.True(
          oldPeakBytes > newPeakBytes,
          $"受控窗口峰值未见净改善：旧 {oldPeakBytes} B vs 新 {newPeakBytes} B"
            + $"（峰值行 {peakRows}，slack 比 {capacityOverCount:F4}，组头 {groupHeaderBytes} B）。");

        // 净改善必须来自【每行载荷收窄】而非把瞬态漏掉：每行省下的 212 B
        // 必须大于瞬态项折算到每行的开销，否则"改善"可能只是记账口径造成的假象。
        var perRowSaving = (long)frozenOldPlanWidthBytes - newPlanWidthBytes;
        Assert.True(
          perRowSaving > sortRowWidth,
          $"每行仅省 {perRowSaving} B，不足以盖过排序行瞬态 {sortRowWidth} B/行。");

        // 组头确实是【新增】成本，且必须已被计入上式 —— 否则"净改善"可能来自漏项。
        Assert.True(
          (long)windowSlots * groupHeaderBytes > (long)windowSlots * IntPtr.Size,
          "组头数组必须比旧的指针数组更贵；若不然，本账的组头项没有意义，需重新审查。");
        Assert.True(groupHeaderBytes > 0);
        Assert.True(newPlanWidthBytes < frozenOldPlanWidthBytes);
        Assert.True(chargedNewPlanRows >= peakRows);
        Assert.True(ledger.PeakWindowGroups > 0 && ledger.PeakWindowGroups <= windowSlots);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    // 窗口行数上界。复制自产品常量，理由是：本测试断言的是"内部预算"这一契约值，
    // 若产品调整了窗口规模，这条断言本就应该跟着重新审视。
    private const int WindowMaxRowsProbe = 131_072;

    // 窗口组数上界。用于区分冲刷究竟由【行】还是【组】触发：
    // 只有 peakGroups < 64 且 peakRows 越界，才能唯一归因于行上界。
    private const int WindowMaxGroupsProbe = 64;

    // 排序行是私有嵌套类型：用反射取其实测宽度，而不是在测试里复制一份布局常量
    // （复制出的常量会与产品代码各自漂移，那样的护栏拦不住任何东西）。
    private static int PlanSortRowWidth()
    {
        var rowType = typeof(NLCPGBuilder).GetNestedType("PlanSortRow", BindingFlags.NonPublic);
        Assert.NotNull(rowType);
        var sizeOf = typeof(Unsafe).GetMethods()
          .Single(method => method.Name == "SizeOf"
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 0);
        return (int)sizeOf.MakeGenericMethod(rowType!).Invoke(null, null)!;
    }

    private static InterproceduralPlanCapacityLedger BuildLedger(int callCount, int dop)
    {
        var builder = Build(
          CreateOptions(10000) with { MaxDegreeOfParallelism = dop },
          CpgBuilderSources.InterproceduralPlanCapacityPressure(callCount),
          "m1-slot-capacity.cs");
        return builder.LastInterproceduralPlanCapacity;
    }

    private static InterproceduralPlanCapacityLedger BuildLedgerForSource(
      string source,
      int budget,
      string filePath)
    {
        return Build(CreateOptions(budget), source, filePath).LastInterproceduralPlanCapacity;
    }

    private static NLCPGBuilder Build(
      NLCPGBuilderOptions options,
      string source,
      string filePath)
    {
        var builder = new NLCPGBuilder(options);
        builder.BuildFromSource(source, filePath);
        return builder;
    }

    // 跨过程桥的规范化描述（源/目标节点 + 桥种类 + 上下文），用于比较两张图是否逐边相同。
    // 不做任何排序：若顺序变化，本描述串也会变化 —— 这正是 N4 想抓住的静默错边形态。
    private static string DescribeBridges(NLCPGGraph graph)
    {
        return string.Join(
          "\n",
          graph.Edges
            .Where(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
            .Select(edge => string.Join(
              "|",
              edge.SourceNodeId,
              edge.TargetNodeId,
              edge.StructuredLabel?.StableKey ?? string.Empty,
              edge.ContextId?.Value ?? string.Empty)));
    }

    // record struct 的实例字段名形如 <SourceNode>k__BackingField。
    private static string NormalizeRecordFieldName(string name)
    {
        var start = name.IndexOf('<');
        if (start < 0)
        {
            return name;
        }

        var end = name.IndexOf('>', start);
        return end < 0 ? name : name[(start + 1)..end];
    }

    private static NLCPGBuilderOptions CreateOptions(int budget)
    {
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
            InterproceduralDataFlowOptions = new NLCPGInterproceduralDataFlowOptions(
              MaxCallTargetsPerSite: 1,
              MaxBoundaryEdgesPerMethod: budget),
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        };
    }
}
