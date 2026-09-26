using NLCPG.Builder;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R-1** 的验证：<see cref="StageDependencyTable"/> 必须能**判定**阶段序列是否合法，
/// 并且必须**认出真实存在过的那次回归**。
/// <para>
/// <b>为什么这些用例值得写：</b>后置 pass 的依赖是隐式的，被破坏时表现为
/// <b>静默少边</b>或<b>整阶段静默跳过</b>（不抛异常、不打日志）。
/// 轮次 19 实测对调 `Dominance`/`ControlDependence` 两行 ⇒ 全量 <c>~Cpg</c> 由 548/548
/// 变为 <b>5 失败</b>（附录 Q.4）。本表的作用是让同类改动**在 worker 启动前**被拒绝。
/// </para>
/// <para>
/// ⚠ 本文件只测**校验设施**本身（纯函数、确定性），不依赖构建产物，
/// 因此不会重蹈附录 N.4/P.4 的「夹具选错观测面」覆辙。
/// </para>
/// </summary>
public sealed class StageDependencyTableTests
{
    private static readonly StageDependencyTable.Stage[] CorrectOrder =
    [
        StageDependencyTable.Stage.CallGraph,
        StageDependencyTable.Stage.MemberAccess,
        StageDependencyTable.Stage.ControlFlow,
        StageDependencyTable.Stage.DataFlow,
        StageDependencyTable.Stage.InterproceduralDataFlow,
        StageDependencyTable.Stage.Dominance,
        StageDependencyTable.Stage.ControlDependence,
    ];

    /// <summary>与 `NLCPGBuilder.cs` 中实际书写顺序一致的序列必须通过。</summary>
    [Fact]
    public void TryValidateOrder_WhenOrderMatchesTheBuilder_ReturnsTrue()
    {
        Assert.True(
          StageDependencyTable.TryValidateOrder(CorrectOrder, out var reason),
          $"当前书写顺序应合法，但被拒绝：{reason}");
    }

    /// <summary>
    /// **核心用例：复现轮次 19 的真实回归**——对调 `Dominance` 与 `ControlDependence`。
    /// <para>
    /// 这正是当时把全量 `~Cpg` 从 548/548 打成 5 失败的那个改动。
    /// 本用例证明该改动现在会被**校验拒绝**，而不是等到测试跑完才发现。
    /// </para>
    /// </summary>
    [Fact]
    public void TryValidateOrder_WhenDominanceAndControlDependenceAreSwapped_ReturnsFalse()
    {
        var swapped = new[]
        {
            StageDependencyTable.Stage.CallGraph,
            StageDependencyTable.Stage.MemberAccess,
            StageDependencyTable.Stage.ControlFlow,
            StageDependencyTable.Stage.DataFlow,
            StageDependencyTable.Stage.InterproceduralDataFlow,
            StageDependencyTable.Stage.ControlDependence,   // ← 被提前
            StageDependencyTable.Stage.Dominance,
        };

        Assert.False(
          StageDependencyTable.TryValidateOrder(swapped, out var reason),
          "对调 Dominance/ControlDependence 必须被拒绝（这正是轮次 19 的真实回归）。");

        Assert.NotNull(reason);
        Assert.Contains("ControlDependence", reason, StringComparison.Ordinal);
        Assert.Contains("Dominance", reason, StringComparison.Ordinal);

        // 失败原因必须写明【实测后果】，否则使用者只知顺序错、不知危害。
        Assert.Contains("静默", reason, StringComparison.Ordinal);
    }

    /// <summary>依赖④：`DataFlow` 前置于 `ControlFlow` 必须被拒绝。</summary>
    [Fact]
    public void TryValidateOrder_WhenDataFlowPrecedesControlFlow_ReturnsFalse()
    {
        var swapped = new[]
        {
            StageDependencyTable.Stage.CallGraph,
            StageDependencyTable.Stage.MemberAccess,
            StageDependencyTable.Stage.DataFlow,        // ← 提前到 ControlFlow 之前
            StageDependencyTable.Stage.ControlFlow,
        };

        Assert.False(StageDependencyTable.TryValidateOrder(swapped, out var reason));
        Assert.NotNull(reason);
        Assert.Contains("DataFlow", reason, StringComparison.Ordinal);
    }

    /// <summary>依赖①/⑥：`DataFlow` 前置于 `CallGraph` 必须被拒绝。</summary>
    [Fact]
    public void TryValidateOrder_WhenDataFlowPrecedesCallGraph_ReturnsFalse()
    {
        var swapped = new[]
        {
            StageDependencyTable.Stage.MemberAccess,
            StageDependencyTable.Stage.ControlFlow,
            StageDependencyTable.Stage.DataFlow,
            StageDependencyTable.Stage.CallGraph,       // ← 被推到最后
        };

        Assert.False(StageDependencyTable.TryValidateOrder(swapped, out _));
    }

    /// <summary>
    /// **能力未开启的阶段整体不运行**，故其缺失不构成违反。
    /// <para>
    /// 这条防止校验过严：只请求 `DataFlow`（不含 `Cfg`）时，序列里没有 `ControlFlow`，
    /// 但依赖④要求 `ControlFlow` 先于 `DataFlow`——按"省略即已满足"处理才正确。
    /// </para>
    /// </summary>
    [Fact]
    public void TryValidateOrder_WhenOmittedStagesAreAbsent_ReturnsTrue()
    {
        var dataFlowOnly = new[]
        {
            StageDependencyTable.Stage.CallGraph,
            StageDependencyTable.Stage.DataFlow,
        };

        Assert.True(
          StageDependencyTable.TryValidateOrder(dataFlowOnly, out var reason),
          $"未请求的阶段缺席不应算违反，但被拒绝：{reason}");
    }

    /// <summary>同一阶段重复出现必须被拒绝（意味着状态被覆盖或产物重复发布）。</summary>
    [Fact]
    public void TryValidateOrder_WhenStageRepeats_ReturnsFalse()
    {
        var repeated = new[]
        {
            StageDependencyTable.Stage.CallGraph,
            StageDependencyTable.Stage.DataFlow,
            StageDependencyTable.Stage.DataFlow,
        };

        Assert.False(StageDependencyTable.TryValidateOrder(repeated, out var reason));
        Assert.NotNull(reason);
        Assert.Contains("多次", reason, StringComparison.Ordinal);
    }

    /// <summary>空序列合法（无任何后置 pass 被请求）。</summary>
    [Fact]
    public void TryValidateOrder_WhenOrderIsEmpty_ReturnsTrue()
    {
        Assert.True(StageDependencyTable.TryValidateOrder([], out _));
    }

    /// <summary>
    /// 表中的每条依赖都必须是**已验证过的 6 条之一**，防止有人把"猜测的依赖"写进表里。
    /// <para>
    /// 判据：本用例固定<b>前置关系图</b>本身。新增依赖必须同时更新本用例与附录 O/P，
    /// 从而强制"依赖表变更需留证据"。
    /// </para>
    /// </summary>
    [Fact]
    public void PredecessorsOf_MatchesTheSixMeasuredDependencies()
    {
        Assert.Equal([StageDependencyTable.Stage.Operation], StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.CallGraph));
        Assert.Equal([StageDependencyTable.Stage.Syntax], StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.Operation));
        Assert.Equal([StageDependencyTable.Stage.CallGraph], StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.MemberAccess));
        Assert.Equal([StageDependencyTable.Stage.MemberAccess], StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.ControlFlow));

        // 依赖④：DataFlow ← ControlFlow；依赖①/⑥：DataFlow ← CallGraph。
        Assert.Equal(
          [StageDependencyTable.Stage.CallGraph, StageDependencyTable.Stage.ControlFlow],
          StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.DataFlow));

        // 依赖②：快照内容依赖（附录 Q.3 已推翻旧的"reducer 必须完成"表述）。
        Assert.Equal(
          [StageDependencyTable.Stage.DataFlow],
          StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.InterproceduralDataFlow));

        // ❗ 依赖③：本次回归的根因。
        Assert.Equal(
          [StageDependencyTable.Stage.Dominance],
          StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.ControlDependence));

        Assert.Equal(
          [StageDependencyTable.Stage.InterproceduralDataFlow],
          StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.Dominance));
    }

    /// <summary>表自身必须无环——否则"合法序列"根本不存在。</summary>
    [Fact]
    public void AllStages_FormsADirectedAcyclicGraph()
    {
        var stages = StageDependencyTable.AllStages;
        var visitState = new Dictionary<StageDependencyTable.Stage, int>();

        foreach (var stage in stages)
        {
            Assert.False(
              HasCycle(stage, visitState),
              $"阶段依赖表中存在环，起点 {stage}。");
        }

        static bool HasCycle(
          StageDependencyTable.Stage stage,
          Dictionary<StageDependencyTable.Stage, int> state)
        {
            if (state.TryGetValue(stage, out var existing))
            {
                // 1 = 正在访问（回边 ⇒ 环）；2 = 已完成。
                return existing == 1;
            }

            state[stage] = 1;
            foreach (var predecessor in StageDependencyTable.PredecessorsOf(stage))
            {
                if (HasCycle(predecessor, state))
                {
                    return true;
                }
            }

            state[stage] = 2;
            return false;
        }
    }
}
