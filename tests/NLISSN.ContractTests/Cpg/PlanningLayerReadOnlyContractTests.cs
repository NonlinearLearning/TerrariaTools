using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P 附录 R.3「分层」的验证：**L0 规划层只读**。
/// <para>
/// <b>为什么单独验证"分层"这一项：</b>四要素中 ①（静态规划时点）②（结果类型）
/// ③（配额作用域）都已落地并被测，唯独 ④「分层与窗口外依赖」中的**分层**此前
/// <b>只有一段注释</b>在声称"整条规划链是纯函数…两者均不写图"
/// （<c>NLCPGBuilder.cs</c> 规划相位处）。那正是附录 N.4/P.4 反复踩到的形态：
/// <b>把一条性质写成声明，而不是机制</b>——该性质一旦被破坏，唯一发现途径是
/// "某个测试恰好断言了图内容"，而多数阶段并无这类断言。
/// </para>
/// <para>
/// 故本文件测两件互不可替代的事：
/// </para>
/// <list type="number">
/// <item><b>守卫真的会拒绝</b>：只读窗口内构图必须抛出（否则"不变量"是空话）。</item>
/// <item><b>窗口真的开过</b>：规划相位必须进入该窗口（否则守卫永不执行，
/// 与"没有守卫"在观测上不可区分——这正是附录 U 首版"给代码改名当成前移"的同型错误）。</item>
/// </list>
/// </summary>
public sealed class PlanningLayerReadOnlyContractTests
{
    /// <summary>规划相位已覆盖的阶段名（附录 V/X）。</summary>
    private static readonly string[] PlanningCoveredStages =
    [
        "CallGraph",
        "MemberAccess",
        "ControlFlow",
        "Dominance",
        "DataFlow",
    ];

    private const string Source = """
        namespace Demo;

        public sealed class Counter
        {
            public int Value { get; set; }

            public int Bump(int delta) => Value + delta;
        }

        public sealed class Caller
        {
            public int Use(Counter counter)
            {
                if (counter.Value > 0)
                {
                    return counter.Bump(2);
                }

                return counter.Value;
            }
        }
        """;

    /// <summary>
    /// **只读窗口内构图必须抛出。**
    /// <para>
    /// 这是守卫本身的判别力测试：若把守卫删掉，本用例立刻失败。
    /// 断言的是**异常类型与成因**（消息须点明分层），而不是"抛了就行"——
    /// 否则图被冻结等其他原因抛出的异常也会让本用例通过。
    /// </para>
    /// </summary>
    [Fact]
    public void AddNode_InsideReadOnlyWindow_ThrowsNamingTheLayerViolation()
    {
        var graph = new NLCPGGraph();
        graph.EnterReadOnlyWindow();

        var exception = Assert.Throws<InvalidOperationException>(
          () => graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.M")));

        Assert.Contains("R.3", exception.Message);
        Assert.Contains("只读窗口", exception.Message);
    }

    /// <summary>
    /// **`AddEdge` 同样被拦住。**
    /// <para>
    /// 单独测一条边路径不是冗余：守卫位于 <c>EnsureMutable</c>（各构图入口的唯一收口），
    /// 但"收口存在"是**实现细节**；若将来有人给某个入口加一条绕过收口的快路径，
    /// 只测 <c>AddNode</c> 不会发现。故对独立入口各测一条。
    /// </para>
    /// </summary>
    [Fact]
    public void AddEdge_InsideReadOnlyWindow_Throws()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.A"));
        var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.B"));

        graph.EnterReadOnlyWindow();

        Assert.Throws<InvalidOperationException>(
          () => graph.AddEdge(source, target, NLCPGEdgeKind.SyntaxChild));
    }

    /// <summary>
    /// **退出窗口后构图恢复正常。**
    /// <para>
    /// 这条防的是"守卫永久生效"这种过度收紧：若窗口只进不出，
    /// 执行相位（L2 归并层）的合法构图会全部误抛，整个构建失效。
    /// 即窗口必须是**窗口**，而不是全局开关。
    /// </para>
    /// </summary>
    [Fact]
    public void AddNode_AfterExitingReadOnlyWindow_Succeeds()
    {
        var graph = new NLCPGGraph();
        graph.EnterReadOnlyWindow();
        graph.ExitReadOnlyWindow();

        var node = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.M"));

        Assert.NotNull(node);
        Assert.False(graph.IsInReadOnlyWindow);
    }

    /// <summary>
    /// **窗口是深度计数、可安全嵌套。**
    /// <para>
    /// 用 <c>bool</c> 实现时，内层退出会把外层窗口一并关掉，从而**静默**留下一个
    /// 不再受保护的外层窗口——这类"嵌套退化"缺陷在单层用例下完全不可见。
    /// </para>
    /// </summary>
    [Fact]
    public void ReadOnlyWindow_WhenNested_StaysClosedUntilTheOutermostExit()
    {
        var graph = new NLCPGGraph();
        graph.EnterReadOnlyWindow();
        graph.EnterReadOnlyWindow();
        graph.ExitReadOnlyWindow();

        // 内层已退出，但外层仍在 ⇒ 必须仍然拒绝。
        Assert.True(graph.IsInReadOnlyWindow);
        Assert.Throws<InvalidOperationException>(
          () => graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.M")));

        graph.ExitReadOnlyWindow();

        Assert.False(graph.IsInReadOnlyWindow);
    }

    /// <summary>
    /// **规划相位确实进入过只读窗口**（否则守卫是死代码）。
    /// <para>
    /// ⚠ 这条与上面几条**不可互相替代**：上面证明"守卫有效"，
    /// 本用例证明"守卫被用上了"。若只测前者，把 <c>EnterReadOnlyWindow</c> 从规划相位
    /// 删掉后所有用例**仍然全绿**——因为没人进窗口，守卫永不执行，
    /// 那个形态与"没有守卫"在行为上**完全等价**。
    /// </para>
    /// <para>
    /// 故用 <see cref="NLCPGGraph.ReadOnlyWindowEntryCount"/> 把"开过窗口"钉成事实：
    /// 正常构建恰好进入 **1** 次（每个 builder 的规划相位开一次）。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenPlanningPhaseRuns_CompletesWithoutAnyGraphWrite()
    {
        var builder = Build();

        var graph = builder.BuildFromSource(Source, "planning-layer.cs");

        Assert.NotNull(graph);
        // 规划确实跑过（否则本断言无意义——空规划相位也能"不写图"）。
        Assert.NotEmpty(builder.PlanSnapshotAtEndOfPlanningPhase!);
        // 且窗口已闭合：执行相位的构图没有被误抛。
        Assert.False(graph.IsInReadOnlyWindow);
    }

    /// <summary>
    /// **只读窗口在规划相位被进入过恰好一次。**
    /// <para>
    /// 这是本文件里唯一能区分"守卫存在且被执行"与"守卫存在但从未执行"的断言。
    /// 若把 <c>EnterReadOnlyWindow</c> 从规划相位删掉，本用例**失败**，
    /// 而其余用例仍会通过——即本用例是防"守卫沦为死代码"的唯一防线。
    /// </para>
    /// <para>
    /// 恰好 <c>1</c>（而非 <c>&gt;= 1</c>）也是刻意的：规划相位每个构建只应开启一次；
    /// 若某次重构把它放进循环或重复调用，重复开启意味着窗口嵌套语义被误用，
    /// 值得立刻失败而不是被 <c>&gt;=</c> 掩盖。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_EntersReadOnlyWindowExactlyOncePerBuild()
    {
        var builder = Build();

        var graph = builder.BuildFromSource(Source, "planning-layer.cs");

        Assert.Equal(1, graph.ReadOnlyWindowEntryCount);
    }

    /// <summary>
    /// **被规划的阶段数不少于 5**——把"窗口开过"钉在一个非平凡的工作量上。
    /// <para>
    /// 若规划相位被掏空（例如有人为规避只读守卫而把 <c>Plan*</c> 调用删掉），
    /// 上一条用例会因为"没有构图"而**依然通过**；本用例会因为规划内容为空而失败。
    /// 两条合起来才排除"为通过守卫而放弃规划"这一退化路径。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_PlanningPhase_CoversAtLeastTheFiveHoistedStages()
    {
        var builder = Build();

        builder.BuildFromSource(Source, "planning-layer.cs");

        var planned = builder.PlanSnapshotAtEndOfPlanningPhase!
          .Select(stage => stage.ToString())
          .ToArray();

        foreach (var stage in PlanningCoveredStages)
        {
            Assert.Contains(stage, planned);
        }
    }

    /// <summary>
    /// **只读窗口只拦"写"，不拦"读"。**
    /// <para>
    /// 这条防的是守卫**挂错位置**：<c>SnapshotMutableFacts</c> 取快照是纯读操作，
    /// 但它与各构图入口共用 <c>EnsureMutable</c> 收口。若不加区分地在收口处拦截，
    /// 规划相位内取一次快照会被误报成"分层被违反"——而真正的问题只是
    /// "守卫挂在了读路径上"。错误的诊断信息比没有守卫更糟：它会把排查引向
    /// "谁在写图"这个根本不存在的问题。
    /// </para>
    /// <para>
    /// 设计依据：附录 R.3 把 L0 定义为「**只读**」——读是 L0 的**职责**，
    /// 被拦的只应是写。
    /// </para>
    /// </summary>
    [Fact]
    public void SnapshotMutableFacts_InsideReadOnlyWindow_IsAllowedBecauseItOnlyReads()
    {
        var graph = new NLCPGGraph();
        graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, "Demo.M"));

        graph.EnterReadOnlyWindow();

        var facts = graph.SnapshotMutableFacts();

        Assert.Single(facts.Nodes);
    }

    /// <summary>
    /// 构造一个请求了全部 5 个已前移阶段所需能力的 builder。
    /// 与 <c>StagePlanContractTests</c> 的夹具保持一致，便于两处结论互相印证。
    /// </summary>
    private static NLCPGBuilder Build() =>
      new(NLCPGBuilderOptions.CreateDefault() with
      {
          MaxDegreeOfParallelism = 1,
          RequestedCapabilities =
          [
              NLCPGCapability.CallTargets,
              NLCPGCapability.Cfg,
              NLCPGCapability.MethodModel,
              NLCPGCapability.Dominance,
              NLCPGCapability.DataFlow,
          ],
      });
}
