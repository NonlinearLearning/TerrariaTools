using NLCPG.Builder;
using NLCPG.Contracts;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R-4** 的验证：配额**声明表**与**执行期观测**的对账（附录 AC）。
/// <para>
/// <c>StageQuotaPolicy.BatchPlanCapableStages</c> 是一张**手写声明**："这 8 个阶段走批次执行器"。
/// 在加入对账之前，它没有任何机制校验它与真实执行一致——若某阶段被漏写，它会被
/// <c>Resolve</c> 归入 <c>NoBatchPlan</c>（"R-3/R-4 对它是 N/A"），从而**静默豁免**整套 R-4 治理，
/// 包括未标定 <c>Dedicated</c> 的拒绝守卫。这与附录 AA 同源：**声明在，机制不在**。
/// </para>
/// <para>
/// <b>本文件的判别力来自两条互补的用例：</b>一条证明**接线存在**（观测集合等于声明集合），
/// 一条证明**判定会拒绝**。只有后者时，"钩子没接上"与"一切正常"在行为上完全不可区分；
/// 只有前者时，"对账逻辑被改成空操作"不可被发现。
/// </para>
/// </summary>
public sealed class StageQuotaDeclarationReconciliationTests
{
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

                counter.Value = 1;
                return counter.Value;
            }
        }
        """;

    /// <summary>
    /// 探测用 id：按<b>与执行器相同的推导规则</b>（前缀 + 阶段名）合成。
    /// <para>
    /// 刻意不新增一个 <c>CpgWorkBatchPerformanceStageId</c> 常量来表达它：
    /// <c>InterproceduralDataFlow</c> **故意不在**那个常量类里（它不提交批次），
    /// 为测试而补一个常量，等于把"不存在的形态"变成生产代码里的声明。
    /// </para>
    /// </summary>
    private const string NoBatchPlanStageProbeId =
      StageQuotaPolicy.WorkBatchStageIdPrefix + nameof(StageDependencyTable.Stage.InterproceduralDataFlow);

    /// <summary>除 <c>InterproceduralDataFlow</c> 之外的全部批次型阶段都请求到。</summary>
    private static readonly NLCPGCapability[] AllBatchCapabilities =
    [
        NLCPGCapability.CallTargets,
        NLCPGCapability.Cfg,
        NLCPGCapability.MethodModel,
        NLCPGCapability.Dominance,
        NLCPGCapability.ControlDependence,
        NLCPGCapability.DataFlow,
    ];

    /// <summary>
    /// **接线判据：观测到的阶段集合必须恰好等于声明表。**
    /// <para>
    /// 这是"对账机制真的被接上"的唯一证据。生产配置下声明表是对的，故 fail-closed
    /// 分支**永远不会触发**——若只有抛出逻辑，把 <c>stageExecutionObserved</c> 钩子删掉
    /// 在行为上与"一切正常"完全一致（附录 N.4/P.4：未被观测面覆盖的断言等于不存在）。
    /// </para>
    /// <para>
    /// 本用例同时钉住两个方向：漏接线 ⇒ 集合为空 ⇒ 失败；
    /// 某阶段被悄悄移出声明表 ⇒ 两集合不等 ⇒ 失败。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_ObservedStageExecutions_MatchDeclaredBatchPlanCapableStages()
    {
        var builder = Build(AllBatchCapabilities);

        var observed = builder.ObservedStageExecutions.Distinct().OrderBy(stage => (int)stage).ToArray();
        var declared = StageQuotaPolicy.BatchPlanCapableStages.OrderBy(stage => (int)stage).ToArray();

        // 先证非空：否则"两个空集合相等"会让本用例在天线被拔掉后依然通过。
        Assert.NotEmpty(observed);
        Assert.Equal(declared, observed);
    }

    /// <summary>
    /// **fail-closed 判据：声明为 <c>NoBatchPlan</c> 的阶段一旦真的提交批次，必须抛出。**
    /// <para>
    /// 判据是**结构层**的而非行为层的：<c>NoBatchPlan</c> 断言"本阶段不提交批次"，
    /// 而执行器此刻正拿着它的批次——两者不可同时为真。故不看产物、不看耗时，直接比对分类。
    /// </para>
    /// <para>
    /// ⚠ 刻意调 <c>NLCPGBuilder.ReconcileObservedStageExecution</c>（生产钩子本身），
    /// 而不是 <c>StageQuotaPolicy</c> 的静态方法：要测的是"构建期对账会拒绝"这一**行为**。
    /// 这也覆盖了"钩子确实被调用"这一接线事实（上一条用例覆盖其参数来源）。
    /// </para>
    /// </summary>
    [Fact]
    public void ReconcileObservedStageExecution_WhenNoBatchPlanStageSubmitsBatches_Throws()
    {
        var builder = Build([NLCPGCapability.InterproceduralDataFlow]);

        // 前提：该阶段确实被声明为"无批次型 plan"——否则本用例在断言一个不存在的形态。
        Assert.Equal(
          StageQuotaBasis.NoBatchPlan,
          builder.LastStageQuotaAllocation.GrantOf(StageDependencyTable.Stage.InterproceduralDataFlow).Basis);

        var exception = Assert.Throws<InvalidOperationException>(
          () => builder.ReconcileObservedStageExecution(NoBatchPlanStageProbeId));

        Assert.Contains("声明表与执行期观测不符", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
          nameof(StageQuotaBasis.NoBatchPlan),
          exception.Message,
          StringComparison.Ordinal);
    }

    /// <summary>
    /// **不得误报**：非阶段标签的 <c>stageId</c> 必须被跳过。
    /// <para>
    /// 执行器是**通用组件**，<c>stageId</c> 对它只是一个标签：测试自造的契约 id
    /// （如 <c>"CPG.WorkBatch.ConsumptionContract"</c>）与分区遥测 id
    /// （<c>"CPG.Syntax.*"</c>，根本到不了执行器）都会经过同一观察点。
    /// 若把"解析不出阶段"当作违规，这些合法调用会被全部误报——
    /// 那时守卫会因**噪声**而被关掉，而不是因为它是错的。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("CPG.WorkBatch.ConsumptionContract")]
    [InlineData("CPG.WorkBatch.WorkerWindowContract")]
    [InlineData("CPG.WorkBatch.NoWindowContract")]
    [InlineData("CPG.WorkBatch")]
    [InlineData("CPG.Syntax.Collection")]
    [InlineData(null)]
    public void ReconcileObservedStageExecution_WhenStageIdIsNotAStage_DoesNotThrow(string? stageId)
    {
        var builder = Build([NLCPGCapability.InterproceduralDataFlow]);

        builder.ReconcileObservedStageExecution(stageId);
    }

    /// <summary>
    /// **规划相位之前不做判定。**<c>Syntax</c>/<c>Operation</c> 在配额表存在**之前**就执行
    /// （附录 V.2：规划相位的作用域是"先于后置 pass 阶段"），那时没有可对账的声明。
    /// <para>
    /// 用 <c>Empty</c> 表直接驱动对账路径：这正是规划相位之前构建所处的状态
    /// （<c>NLCPGBuilder.cs</c> 在相位开始前将 <c>_stageQuotaAllocation</c> 置为 <c>Empty</c>）。
    /// 若这里抛出，全部构建都会在 Syntax 阶段失败。
    /// </para>
    /// </summary>
    [Fact]
    public void ReconcileObservedStageExecution_BeforePlanningPhaseResolves_DoesNotThrow()
    {
        StageQuotaPolicy.ReconcileObservedStageExecution(
          StageQuotaAllocation.Empty,
          NoBatchPlanStageProbeId);
    }

    /// <summary>
    /// 由后缀反解阶段，且**只**认声明表中的阶段——这是对账不引入第二张映射表的依据。
    /// </summary>
    [Fact]
    public void TryResolveDeclaredStage_ResolvesEveryDeclaredStageId()
    {
        foreach (var stage in StageDependencyTable.AllStages)
        {
            var stageId = StageQuotaPolicy.WorkBatchStageIdPrefix + stage;

            Assert.True(StageQuotaPolicy.TryResolveDeclaredStage(stageId, out var resolved));
            Assert.Equal(stage, resolved);
        }
    }

    /// <summary>
    /// **声明表里不得有"没有对应 stageId 字面量"的阶段。**
    /// <para>
    /// 反向钉住映射的可解析性：若某阶段被登记进 <see cref="StageQuotaPolicy.BatchPlanCapableStages"/>
    /// 却没有任何 pass 会以它的名字提交批次，则"声明表 == 观测集合"那条用例会失败，
    /// 但失败信息指向观测；本用例让**成因**直接指向声明表本身。
    /// </para>
    /// </summary>
    [Fact]
    public void BatchPlanCapableStages_EveryDeclaredStage_HasResolvableStageId()
    {
        Assert.NotEmpty(StageQuotaPolicy.BatchPlanCapableStages);

        foreach (var stage in StageQuotaPolicy.BatchPlanCapableStages)
        {
            Assert.True(
              StageQuotaPolicy.TryResolveDeclaredStage(
                StageQuotaPolicy.WorkBatchStageIdPrefix + stage,
                out var resolved),
              $"声明表里的阶段 {stage} 没有可解析的 stageId。");
            Assert.Equal(stage, resolved);
        }
    }

    private static NLCPGBuilder Build(NLCPGCapability[] capabilities)
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = capabilities,
        });

        builder.BuildFromSource(Source, "stage-quota-reconciliation.cs");

        return builder;
    }
}
