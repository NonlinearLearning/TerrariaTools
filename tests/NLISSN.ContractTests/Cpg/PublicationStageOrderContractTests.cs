using NLCPG.Builder;
using NLCPG.Contracts;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R.4 第 3 类（发布依赖）**的机制验证：发布（FreezeQueryIndex / PersistenceWrite）
/// 必须**晚于全部阶段提交**且与阶段执行**不重叠**。
/// <para>
/// <b>为什么单列一个文件：</b>计划 <c>:1836</c> 原先自述第 3 类"仍只有文字描述，无任何机制"。
/// 轮次 31 把它拆成两半后，只有"发布须晚于全部提交且不重叠"这半是**真的没有机制**：
/// 此前 <c>EnsureStageDependencyOrder</c> 只校验**阶段之间**的顺序，而
/// <c>FreezeQueryIndex</c>/<c>PersistenceWrite</c> **不在** <c>StageDependencyTable.Stage</c>
/// 枚举里（它们不是 work-batch 阶段）⇒ 发布动作**落在该表的校验范围之外**。
/// </para>
/// <para>
/// <b>为什么判据必须落在序列层（而非行为层）：</b>本机制按设计**不改变任何行为**——
/// 它在"发布早于校验/与 worker 重叠"这一**本不该发生**的情况下抛出，而生产路径永远
/// 不满足该条件。故"接线存在"与"接线被拔掉"在行为层**不可区分**
/// （附录 X.5 / Y.4 / AA / AC.5 反复踩到的形态）。唯一有判别力的判据是：
/// 发布顺序**真的被记录下来了**，且记录的**内容正确**。
/// </para>
/// <para>
/// ⚠ 另一个必须被挡住的失效方向：<c>_publicationStageOrder</c> 首版**只写不读**，
/// 于是"留证"在观测上完全不存在——"从未记录"与"没有机制"等价。
/// 本文件的用例正是它的读者。
/// </para>
/// </summary>
public sealed class PublicationStageOrderContractTests
{
    private const string Source = """
        namespace Demo;

        public sealed class Sample
        {
            public int Adjust(int value)
            {
                if (value > 0)
                {
                    value += 1;
                }

                return value;
            }
        }
        """;

    /// <summary>
    /// **核心用例：发布顺序必须被记录，且恰为 Freeze → Persistence。**
    /// <para>
    /// <b>为什么这条不可省：</b><c>RecordPublicationStage</c> 若被删掉（或只在其中一处调用），
    /// 其余全部行为用例**依然全绿**——本用例是该机制唯一的判别面。
    /// </para>
    /// <para>
    /// 顺序而非集合：两级发布点之间隔着 <c>ReleaseTransientBuilderState</c> 与分支判断，
    /// 把它们对调会让持久化读到已释放的 builder 状态，故**次序本身**是契约的一部分。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_RecordsPublicationStagesInFreezeThenPersistenceOrder()
    {
        var builder = Build(enablePersistence: true);

        var order = builder.LastPublicationStageOrder;

        Assert.Equal(new[] { "FreezeQueryIndex", "PersistenceWrite" }, order);
    }

    /// <summary>
    /// **未启用持久化时不得谎报写了**——记录必须如实反映"真的执行了"。
    /// <para>
    /// 若把 <c>RecordPublicationStage("PersistenceWrite")</c> 提到
    /// <c>if (_options.Persistence is not null)</c> **之外**，本用例失败而其余用例全绿。
    /// 这与 <c>StageExecutionRecordingTests</c> 的同型用例是同一道理：
    /// 记录一旦无条件追加，就会破坏"缺席 = 未请求"的正确语义。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenPersistenceDisabled_RecordsOnlyFreeze()
    {
        var builder = Build(enablePersistence: false);

        var order = builder.LastPublicationStageOrder;

        Assert.Equal(new[] { "FreezeQueryIndex" }, order);
    }

    /// <summary>
    /// **发布必须晚于全部阶段执行**——这是第 3 类的实质断言，而非只测"记了两个名字"。
    /// <para>
    /// 判据：发布记录**非空**（证明发布确实发生）且**阶段执行序列非空**
    /// （证明确实跑过阶段），二者共同说明"发布发生在一个真实构建的尾部"。
    /// 若发布被提前到阶段执行之前，<c>EnsurePublicationAllowed</c> 会在真实构建中抛出，
    /// 本用例即失败。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_PublishesAfterTheStageExecutionSequenceIsComplete()
    {
        var builder = Build(enablePersistence: true);

        // 非空断言不可省：两个空集合也能"相等"，那会让拔掉记录后依然通过。
        Assert.NotEmpty(builder.LastExecutedStageOrder);
        Assert.NotEmpty(builder.LastPublicationStageOrder);

        // 发布守卫的顺序前提：能走到发布 ⇒ 阶段依赖校验已通过。
        // 这是"发布晚于全部阶段提交"的直接证据（该标志只在校验成功分支置位）。
        Assert.True(
          StageDependencyTable.TryValidateOrder(builder.LastExecutedStageOrder, out var reason),
          $"发布了却未通过阶段依赖校验：{reason}");
    }

    /// <summary>
    /// **同一次构建重复调用不得累积**——发布记录每次构建都必须重置。
    /// <para>
    /// 若 <c>Build</c> 入口漏掉 <c>_publicationStageOrder.Clear()</c>，
    /// 第二次构建会看到 **2** 条记录（上一次的 + 本次的）。本用例是该重置的唯一判别面：
    /// 生产路径只构建一次，故行为层完全看不出这个问题。
    /// </para>
    /// <para>
    /// ⚠ <b>刻意用"未启用持久化"来做本判据（实测得出的修正）：</b>首版用启用持久化的
    /// 构建器连跑两次，期望两次都是 <c>[Freeze, Persistence]</c>——**实测失败**，
    /// 第二次只有 <c>[Freeze]</c>。原因是**正确行为**而非缺陷：持久化点受
    /// <c>!persistenceHit</c> 门控，第二次构建命中同一 <c>StoreRoot</c>/<c>ProfileHash</c>
    /// 的目录后**跳过写盘**。若把该用例写成"两次都必须含 PersistenceWrite"，
    /// 它会去要求一个**不该发生**的写入，把正确行为判成回归。
    /// 故此处只断言"两次完全相同 + 恰为一行"——那才是"重置生效"的最小判据。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenBuilderIsReused_DoesNotAccumulatePublicationRecords()
    {
        var builder = CreateBuilder(enablePersistence: false);

        builder.BuildFromSource(Source, "publication-reuse.cs");
        var first = builder.LastPublicationStageOrder.ToArray();
        builder.BuildFromSource(Source, "publication-reuse.cs");
        var second = builder.LastPublicationStageOrder.ToArray();

        Assert.Equal(first, second);
        // 恰为一行：若入口漏了 Clear，第二次会是两行。
        Assert.Equal(new[] { "FreezeQueryIndex" }, second);
    }

    /// <summary>
    /// **命中持久化目录时不得记录"写过盘"**——记录必须如实反映"真的执行了"。
    /// <para>
    /// 本用例把上一个用例实测到的行为**钉成契约**：第二次构建命中同一
    /// <c>StoreRoot</c>/<c>ProfileHash</c> 后跳过 <c>PersistenceWrite</c>，
    /// 故记录里不得出现它。若把 <c>RecordPublicationStage("PersistenceWrite")</c>
    /// 提到 <c>!persistenceHit</c> 判断**之外**，本用例失败——
    /// 这与 <c>StageExecutionRecordingTests</c> 的"未请求就不该记录"同型。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenPersistenceCatalogHits_DoesNotRecordPersistenceWrite()
    {
        var builder = CreateBuilder(enablePersistence: true);

        builder.BuildFromSource(Source, "publication-hit.cs");
        var first = builder.LastPublicationStageOrder.ToArray();
        builder.BuildFromSource(Source, "publication-hit.cs");
        var second = builder.LastPublicationStageOrder.ToArray();

        // 第一次是真写盘。
        Assert.Equal(new[] { "FreezeQueryIndex", "PersistenceWrite" }, first);
        // 第二次命中目录 ⇒ 跳过写盘 ⇒ 不得谎报。
        Assert.Equal(new[] { "FreezeQueryIndex" }, second);
    }

    /// <summary>
    /// **判定的拒绝分支必须真的会拒绝**——这是附录 AC.5 规则的直接落实。
    /// <para>
    /// <b>为什么必须单列（轮次 31 实测得出）：</b>首轮只测了"接线存在"（发布顺序被记录），
    /// 专项 5/5 全绿。但变异验证把窗口计数改成**恒读 0**（即让非重叠判定永不触发）后，
    /// 这 5 条**依然全绿** ⇒ 拒绝分支是**零覆盖的死代码**。
    /// 生产路径下两个前提到达发布点时必然满足，故拒绝分支无法由任何行为用例触达；
    /// 唯一办法是直接从纯判定函数注入前提。本用例就是那个接缝。
    /// </para>
    /// </summary>
    [Theory]
    // 两个前提都满足 ⇒ 放行。
    [InlineData(true, 0, false)]
    // 顺序前提失败（窗口已归零，仅此一条就足以拒绝）。
    [InlineData(false, 0, true)]
    // 非重叠前提失败（顺序已校验，仍有 1 个窗口存活）。
    [InlineData(true, 1, true)]
    // 非重叠前提失败（多个窗口存活）。
    [InlineData(true, 3, true)]
    // 两条同时失败 ⇒ 仍须拒绝（且必须命中"顺序前提"这条，见下条用例）。
    [InlineData(false, 2, true)]
    public void EvaluatePublicationGate_RejectsExactlyWhenAPreconditionIsViolated(
      bool orderVerified,
      int activeWindows,
      bool expectRejection)
    {
        var reason = NLCPGBuilder.EvaluatePublicationGate("FreezeQueryIndex", orderVerified, activeWindows);

        Assert.Equal(expectRejection, reason is not null);
    }

    /// <summary>
    /// **拒绝消息必须指明是哪一条前提被违反**——否则运维只知"发布被拒"，不知修哪里。
    /// <para>
    /// 两条前提的消息**互不相同**：顺序前提说"尚未通过依赖表校验"，
    /// 非重叠前提说"仍有 N 个窗口存活"并给出修法。若把两条合并成一句通用文案，
    /// 本用例失败——而"会拒绝"本身仍然成立，故这是**独立**于上一条的判据。
    /// </para>
    /// </summary>
    [Fact]
    public void EvaluatePublicationGate_ReportsWhichPreconditionFailed()
    {
        var orderFailure = NLCPGBuilder.EvaluatePublicationGate(
          "PersistenceWrite", stageDependencyOrderVerified: false, activeWorkerWindowCount: 0);
        var overlapFailure = NLCPGBuilder.EvaluatePublicationGate(
          "PersistenceWrite", stageDependencyOrderVerified: true, activeWorkerWindowCount: 2);

        Assert.NotNull(orderFailure);
        Assert.NotNull(overlapFailure);

        // 各自点名自己的成因。
        Assert.Contains("尚未通过", orderFailure);
        Assert.Contains("仍有 2 个", overlapFailure);

        // 且两条消息不可互换（防止"同一句话覆盖两种情况"）。
        Assert.NotEqual(orderFailure, overlapFailure);

        // 被拒绝的阶段名必须出现在消息里（多发布点时能定位是哪一个）。
        Assert.Contains("PersistenceWrite", orderFailure);
        Assert.Contains("PersistenceWrite", overlapFailure);
    }

    /// <summary>
    /// **L1 共享态窗口的接线必须可证伪**（结构/计数层判据）。
    /// <para>
    /// <b>为什么必须有这条（轮次 31 变异实测）：</b>把
    /// <c>sharedStateWindowEnter/Exit</c> 的接线拔掉后，本文件全部 11 条用例
    /// **乃至全量 658 条 <c>~Cpg</c> 用例仍然全绿**——因为该机制按设计**不改变行为**
    /// （今天不是活跃竞争，窗口期只多取一次锁）。这正是附录 X.5/Y.4/AA/AC.5 的教训：
    /// <b>"从未记录"与"没有机制"行为等价</b>。故判据只能落在**计数层**。
    /// </para>
    /// <para>
    /// 同时断言窗口**开完即净**（存活数回到 0）：若非零，发布守卫会在每次构建时抛出，
    /// 而这条断言能在订阅到具体行为失败之前就指出"计数泄漏"这一根因。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_OpensSharedStateWindowAndReturnsItToZero()
    {
        var builder = Build(enablePersistence: false);

        Assert.True(
          builder.SharedStateWindowEnterCount > 0,
          "共享态窗口从未被打开——L1「不共享可变中间态」的接线已失效（行为层看不出）。");
        Assert.Equal(0, builder.SharedStateWindowOpenCount);
    }

    /// <summary>
    /// **复用 builder 时窗口计数必须按构建重置**，且第二次仍会开窗。
    /// <para>
    /// 与 <c>_publicationStageOrder</c> 的复用用例同源：生产路径只构建一次，
    /// 故"累计值跨构建不清"在行为层不可见。本用例钉住"每次构建都重新开窗"。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenBuilderIsReused_ResetsSharedStateWindowCount()
    {
        var builder = CreateBuilder(enablePersistence: false);

        builder.BuildFromSource(Source, "window-reuse.cs");
        var first = builder.SharedStateWindowEnterCount;
        builder.BuildFromSource(Source, "window-reuse.cs");
        var second = builder.SharedStateWindowEnterCount;

        Assert.True(first > 0, "第一次构建未开共享态窗口。");
        Assert.True(second > 0, "第二次构建未开共享态窗口。");
        Assert.Equal(0, builder.SharedStateWindowOpenCount);
    }

    private static NLCPGBuilder Build(bool enablePersistence)
    {
        var builder = CreateBuilder(enablePersistence);
        builder.BuildFromSource(Source, "publication-order.cs");
        return builder;
    }

    private static NLCPGBuilder CreateBuilder(bool enablePersistence)
    {
        var options = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.DataFlow },
        };

        if (enablePersistence)
        {
            // 持久化需要真实的暂存根目录（见 CpgPersistenceStateTests 的既有写法）。
            var root = Path.Combine(
              Path.GetTempPath(),
              "cpg-publication-order",
              Guid.NewGuid().ToString("N"));
            options = options with { Persistence = new CpgPersistenceOptions(root, "publication-order") };
        }

        return new NLCPGBuilder(options);
    }
}
