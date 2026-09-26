using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using NLCPG.Contracts;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R-4** 的验证：配额**作用域**分级（附录 R 四要素第 ③ 项）。
/// <para>
/// <b>本文件刻意不测"额度是多少"</b>——附录 G 已实测不存在可用的 bytes 公式上界
/// （同一行范围下真实载荷跨度 1349×），故任何"额度"断言都会是在断言一个凭空造的常量。
/// 本文件只测三件事：
/// </para>
/// <list type="number">
/// <item>配额分级**确实消费了规划相位的快照**（而不是另算一份）。</item>
/// <item>默认作用域是 <c>Shared</c> ⇒ **不改变**任何既有行为。</item>
/// <item>未标定的 <c>Dedicated</c> 被**拒绝**（fail-closed），而不是静默降级。</item>
/// </list>
/// </summary>
public sealed class StageQuotaScopeContractTests
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
    /// **核心用例：配额分级消费的是规划相位快照。**
    /// <para>
    /// 判据：被授予 <c>PlannedBeforeExecution</c> 依据的阶段集合，必须**恰好等于**
    /// 规划相位快照的键集合。若 R-4 自己另算一份规模来源（例如重新调 <c>Plan*</c>），
    /// 两个集合就可能不一致——本用例正是拦这个。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_QuotaAllocation_ConsumesExactlyThePlanningPhaseSnapshot()
    {
        var builder = Build(new[]
        {
            NLCPGCapability.CallTargets,
            NLCPGCapability.Cfg,
            NLCPGCapability.MethodModel,
            NLCPGCapability.Dominance,
        });

        var snapshot = builder.PlansAtEndOfPlanningPhase
          ?? throw new InvalidOperationException("未记录规划相位的 plan 副本。");

        var allocation = builder.LastStageQuotaAllocation;

        // 依据为「规划前已知规模」的阶段 === 规划相位快照的键集合。
        Assert.Equal(
          snapshot.Keys.OrderBy(stage => (int)stage).ToArray(),
          allocation.ScaleKnownBeforeExecution.OrderBy(stage => (int)stage).ToArray());
    }

    /// <summary>
    /// 规划相位覆盖到的每一阶段，其配额依据都必须是 <c>PlannedBeforeExecution</c>；
    /// 且它们必须是本轮已前移的那 4 个。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenAllPlannableRequested_QuotaBasisIsPlannedBeforeExecutionForTheFour()
    {
        var builder = Build(new[]
        {
            NLCPGCapability.CallTargets,
            NLCPGCapability.Cfg,
            NLCPGCapability.MethodModel,
            NLCPGCapability.Dominance,
        });

        var allocation = builder.LastStageQuotaAllocation;

        foreach (var stage in new[]
                 {
                     StageDependencyTable.Stage.CallGraph,
                     StageDependencyTable.Stage.ControlFlow,
                     StageDependencyTable.Stage.MemberAccess,
                     StageDependencyTable.Stage.Dominance,
                 })
        {
            var grant = allocation.GrantOf(stage);

            Assert.Equal(StageQuotaBasis.PlannedBeforeExecution, grant.Basis);
            Assert.True(grant.ScaleWasKnownBeforeExecution);
            Assert.NotNull(grant.EstimatedBytesAtPlanningTime);
        }
    }

    /// <summary>
    /// **"缺席"的成因必须分类**：<c>ControlDependence</c> 的配额依据是
    /// <c>DeferredPlanning</c>（依赖运行期产物），**不是** <c>PlannedBeforeExecution</c>。
    /// <para>
    /// 这条防的是把"未出现在快照里"一律当成"已前移但规模为 0"——那正是
    /// 附录 N.4/P.4/S.2 反复踩到的"观测面成因不分类"形态。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlDependenceRequested_QuotaBasisIsDeferredNotPlanned()
    {
        var builder = Build(new[] { NLCPGCapability.ControlDependence });

        var grant = builder.LastStageQuotaAllocation.GrantOf(StageDependencyTable.Stage.ControlDependence);

        Assert.Equal(StageQuotaBasis.DeferredPlanning, grant.Basis);
        Assert.False(grant.ScaleWasKnownBeforeExecution);
        Assert.Null(grant.EstimatedBytesAtPlanningTime);
    }

    /// <summary>
    /// <c>InterproceduralDataFlow</c> 没有批次型 plan ⇒ 依据为 <c>NoBatchPlan</c>（**N/A**），
    /// 与 <c>DeferredPlanning</c>（**待办**）区分开。
    /// <para>
    /// 这两者在文档里极易被合并成一句"暂不支持"，而在行动上完全不同：
    /// 前者不需要做，后者需要做。故用测试把它们分开。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenInterproceduralRequested_QuotaBasisIsNoBatchPlanNotDeferred()
    {
        var builder = Build(new[] { NLCPGCapability.InterproceduralDataFlow });

        var grant = builder.LastStageQuotaAllocation.GrantOf(
          StageDependencyTable.Stage.InterproceduralDataFlow);

        Assert.Equal(StageQuotaBasis.NoBatchPlan, grant.Basis);
        Assert.False(grant.ScaleWasKnownBeforeExecution);
    }

    /// <summary>
    /// **默认不引入新额度**：所有阶段的请求作用域都是 <c>Shared</c>，
    /// 故授予表里**没有**任何 <c>Dedicated</c> ⇒ 与既有单池行为逐字一致。
    /// </summary>
    [Fact]
    public void BuildFromSource_ByDefault_NoStageIsGrantedDedicatedScope()
    {
        var builder = Build(new[]
        {
            NLCPGCapability.CallTargets,
            NLCPGCapability.Cfg,
            NLCPGCapability.MethodModel,
            NLCPGCapability.Dominance,
            NLCPGCapability.ControlDependence,
            NLCPGCapability.InterproceduralDataFlow,
        });

        var allocation = builder.LastStageQuotaAllocation;

        Assert.Empty(allocation.DedicatedStages);
        Assert.All(
          allocation.Grants,
          grant => Assert.Equal(StageQuotaScope.Shared, grant.GrantedScope));
    }

    /// <summary>
    /// **九个阶段的成因一次钉死（表驱动）。**
    /// <para>
    /// 上文的用例各自只断言一两个阶段，故"某个阶段被悄悄改了成因"仍可能漏网。
    /// 本用例把**全部**阶段与期望成因列表比对——新增阶段时它会立刻失败，
    /// 从而强制作者**显式决定**该阶段的成因，而不是让它落进某个默认分支。
    /// </para>
    /// <para>
    /// 表本身即 G0-P 四要素第 ③ 项在本轮的**权威现状快照**（W.4 / X.6）。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_EveryStage_HasItsDeclaredQuotaBasis()
    {
        var builder = Build(new[]
        {
            NLCPGCapability.CallTargets,
            NLCPGCapability.Cfg,
            NLCPGCapability.MethodModel,
            NLCPGCapability.Dominance,
            NLCPGCapability.ControlDependence,
            NLCPGCapability.DataFlow,
            NLCPGCapability.InterproceduralDataFlow,
        });

        var allocation = builder.LastStageQuotaAllocation;

        // 期望成因（逐条对应 W.4 表）：
        var expected = new Dictionary<StageDependencyTable.Stage, StageQuotaBasis>
        {
            // 规划相位已产出 plan（5 个；DataFlow 见附录 X）。
            [StageDependencyTable.Stage.CallGraph] = StageQuotaBasis.PlannedBeforeExecution,
            [StageDependencyTable.Stage.MemberAccess] = StageQuotaBasis.PlannedBeforeExecution,
            [StageDependencyTable.Stage.ControlFlow] = StageQuotaBasis.PlannedBeforeExecution,
            [StageDependencyTable.Stage.Dominance] = StageQuotaBasis.PlannedBeforeExecution,
            [StageDependencyTable.Stage.DataFlow] = StageQuotaBasis.PlannedBeforeExecution,
            // 规划相位**之前**就已跑完（"时候已过"）。
            [StageDependencyTable.Stage.Syntax] = StageQuotaBasis.ExecutedBeforePlanningPhase,
            [StageDependencyTable.Stage.Operation] = StageQuotaBasis.ExecutedBeforePlanningPhase,
            // 依赖前序阶段运行期产物（"时候未到"）。
            [StageDependencyTable.Stage.ControlDependence] = StageQuotaBasis.DeferredPlanning,
            // 无批次型 plan（纯转发）。
            [StageDependencyTable.Stage.InterproceduralDataFlow] = StageQuotaBasis.NoBatchPlan,
        };

        // 表必须覆盖全部阶段——否则"比对通过"可能只是因为两边都漏了同一个阶段。
        Assert.Equal(
          StageDependencyTable.AllStages.OrderBy(stage => (int)stage).ToArray(),
          expected.Keys.OrderBy(stage => (int)stage).ToArray());

        foreach (var (stage, basis) in expected)
        {
            Assert.Equal(basis, allocation.GrantOf(stage).Basis);
        }
    }

    /// <summary>授予表必须覆盖**全部已登记阶段**（含未请求的），否则配额视图会有静默空洞。</summary>
    [Fact]
    public void BuildFromSource_QuotaAllocation_CoversEveryRegisteredStage()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        var allocation = builder.LastStageQuotaAllocation;

        Assert.Equal(
          StageDependencyTable.AllStages.OrderBy(stage => (int)stage).ToArray(),
          allocation.Grants.Select(grant => grant.Stage).OrderBy(stage => (int)stage).ToArray());
    }

    /// <summary>
    /// **fail-closed：未标定的 <c>Dedicated</c> 被拒绝。**
    /// <para>
    /// 这是 R-4 的关键守卫：附录 R.2 ③ 写明 <c>Dedicated</c>「需实测标定后才可启用」。
    /// 若只把该前提写在文档里，把它改成 <c>Dedicated</c> 就是一个**不可被拒绝**的操作。
    /// 本用例证明：没有标定时，解析**抛出**而不是静默按 <c>Shared</c> 放过。
    /// </para>
    /// </summary>
    [Fact]
    public void Resolve_WhenDedicatedRequestedWithoutCalibration_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
          () => ResolveWithDedicated(StageDependencyTable.Stage.ControlFlow, calibrations: null));

        Assert.Contains("没有实测标定", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 规划依据不是 <c>PlannedBeforeExecution</c> 时，**即使有标定**也不得授予 <c>Dedicated</c>——
    /// 因为窗口开始前规模未知，无从据以配额度。
    /// </summary>
    [Fact]
    public void Resolve_WhenDedicatedRequestedForDeferredStage_Throws()
    {
        var calibrations = new[]
        {
            new StageQuotaCalibration(
              StageDependencyTable.Stage.ControlDependence,
              MeasuredPeakBytes: 1024,
              Evidence: "synthetic"),
        };

        var exception = Assert.Throws<InvalidOperationException>(
          () => ResolveWithDedicated(
            StageDependencyTable.Stage.ControlDependence,
            calibrations,
            stageHasPlan: false));

        Assert.Contains("窗口开始前规模未知", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>标定值必须为正——用 0 或负数冒充标定同样被拒绝。</summary>
    [Fact]
    public void Resolve_WhenCalibrationIsNonPositive_Throws()
    {
        var calibrations = new[]
        {
            new StageQuotaCalibration(
              StageDependencyTable.Stage.ControlFlow,
              MeasuredPeakBytes: 0,
              Evidence: "synthetic"),
        };

        var exception = Assert.Throws<InvalidOperationException>(
          () => ResolveWithDedicated(StageDependencyTable.Stage.ControlFlow, calibrations));

        Assert.Contains("必须为正", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>标定必须带证据出处——无出处的标定无法复核，等同于没有标定。</summary>
    [Fact]
    public void Resolve_WhenCalibrationHasNoEvidence_Throws()
    {
        var calibrations = new[]
        {
            new StageQuotaCalibration(
              StageDependencyTable.Stage.ControlFlow,
              MeasuredPeakBytes: 1024,
              Evidence: "   "),
        };

        var exception = Assert.Throws<InvalidOperationException>(
          () => ResolveWithDedicated(StageDependencyTable.Stage.ControlFlow, calibrations));

        Assert.Contains("证据出处", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>有完整标定时，<c>Dedicated</c> 路径**不再**抛出——守卫不是无条件拒绝。</summary>
    [Fact]
    public void Resolve_WhenCalibrationIsComplete_GrantsDedicated()
    {
        var calibrations = new[]
        {
            new StageQuotaCalibration(
              StageDependencyTable.Stage.ControlFlow,
              MeasuredPeakBytes: 1024,
              Evidence: "synthetic"),
        };

        var allocation = ResolveWithDedicated(StageDependencyTable.Stage.ControlFlow, calibrations);

        Assert.Equal(
          [StageDependencyTable.Stage.ControlFlow],
          allocation.DedicatedStages);
    }

    /// <summary>
    /// 驱动 <see cref="StageQuotaPolicy.Resolve"/> 的真实路径，把指定阶段声明为 <c>Dedicated</c>。
    /// <para>
    /// ⚠ 刻意走 <c>Resolve</c> 而不是某个私有校验函数：要测的是"配额解析会拒绝未标定的独占请求"
    /// 这一**行为**，而不是"某个函数会抛"。后者在解析路径改动后会静默失去判别力。
    /// </para>
    /// </summary>
    /// <param name="stage">被声明为 <c>Dedicated</c> 的阶段。</param>
    /// <param name="calibrations">提供的标定。</param>
    /// <param name="stageHasPlan">
    /// 该阶段是否在规划相位产出了 plan。传 <c>false</c> 可复现
    /// "批次型阶段但规划延迟"（basis = <c>DeferredPlanning</c>）这一形态。
    /// </param>
    private static StageQuotaAllocation ResolveWithDedicated(
      StageDependencyTable.Stage stage,
      IReadOnlyList<StageQuotaCalibration>? calibrations,
      bool stageHasPlan = true)
    {
        var plans = new Dictionary<StageDependencyTable.Stage, IStagePlan<CpgWorkBatch>>();
        if (stageHasPlan)
        {
            plans[stage] = new StagePlan(stage, Array.Empty<CpgWorkBatch>());
        }

        var requested = new Dictionary<StageDependencyTable.Stage, StageQuotaScope>
        {
            [stage] = StageQuotaScope.Dedicated,
        };

        return StageQuotaPolicy.Resolve(
          plans,
          StageQuotaPolicy.BatchPlanCapableStages,
          calibrations,
          requested);
    }

    /// <summary>
    /// 本轮**不推导任何字节额度**：授予表里记录的只是规划期估算值，
    /// 且它与"该阶段被授予的作用域"无关——两者不得被当成同一个东西。
    /// </summary>
    [Fact]
    public void BuildFromSource_EstimatedBytesAtPlanningTime_IsRecordedButIsNotAQuota()
    {
        var builder = Build(new[]
        {
            NLCPGCapability.CallTargets,
            NLCPGCapability.Cfg,
            NLCPGCapability.MethodModel,
            NLCPGCapability.Dominance,
        });

        var grant = builder.LastStageQuotaAllocation.GrantOf(StageDependencyTable.Stage.ControlFlow);

        // 估算值被留证……
        Assert.NotNull(grant.EstimatedBytesAtPlanningTime);
        Assert.True(grant.EstimatedBytesAtPlanningTime >= 0);

        // ……但它不改变作用域：仍是 Shared，且没有任何阶段被授予 Dedicated。
        Assert.Equal(StageQuotaScope.Shared, grant.GrantedScope);
        Assert.Empty(builder.LastStageQuotaAllocation.DedicatedStages);
    }

    /// <summary>
    /// **`Syntax`/`Operation` 的成因是"早已执行完"，不是"规划被推迟"。**
    /// <para>
    /// <b>为什么这条必须存在：</b>两者都走批次执行器、又都不在规划相位快照里，
    /// 故朴素实现会把它们归入 <c>DeferredPlanning</c>——即描述成
    /// "窗口开始前规模未知"。但事实相反：它们在规划相位开启**之前**就已跑完
    /// （<c>NLCPGBuilder.cs:374</c> / <c>:401</c> vs 相位开启于 <c>:452</c>）。
    /// </para>
    /// <para>
    /// 该差别**不是措辞问题**：<c>DeferredPlanning</c> 意味着"将来会前移"（有后续工作），
    /// <c>ExecutedBeforePlanningPhase</c> 意味着"R-3 对它不适用"（无后续工作）。
    /// 把后者说成前者，会凭空造出一项永远不会被完成的待办。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_SyntaxAndOperation_AreClassifiedAsExecutedBeforePlanningPhase()
    {
        var builder = Build(new[]
        {
            NLCPGCapability.CallTargets,
            NLCPGCapability.Cfg,
            NLCPGCapability.MethodModel,
            NLCPGCapability.Dominance,
            NLCPGCapability.DataFlow,
        });

        var allocation = builder.LastStageQuotaAllocation;

        foreach (var stage in new[]
                 {
                     StageDependencyTable.Stage.Syntax,
                     StageDependencyTable.Stage.Operation,
                 })
        {
            var grant = allocation.GrantOf(stage);

            Assert.Equal(StageQuotaBasis.ExecutedBeforePlanningPhase, grant.Basis);
            // 它们**确实**走批次执行器，故必须仍在"批次型"集合里——
            // 两个集合都必需，缺一就会误分类。
            Assert.Contains(stage, StageQuotaPolicy.BatchPlanCapableStages);
            Assert.Contains(stage, StageQuotaPolicy.StagesExecutedBeforePlanningPhase);
            // 规模在规划相位之前已成事实，但**不是**通过 plan 得知的。
            Assert.False(grant.ScaleWasKnownBeforeExecution);
            Assert.Null(grant.EstimatedBytesAtPlanningTime);
        }
    }

    /// <summary>
    /// `ControlDependence` 才是真正的 `DeferredPlanning`——与上面两个阶段的成因不同。
    /// <para>
    /// 本用例与上一条**成对**：只测一边无法排除"所有无 plan 阶段都被归为同一成因"。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_ControlDependence_RemainsDistinctFromExecutedBeforePlanningPhase()
    {
        var builder = Build(new[] { NLCPGCapability.ControlDependence });

        var allocation = builder.LastStageQuotaAllocation;

        Assert.Equal(
          StageQuotaBasis.DeferredPlanning,
          allocation.GrantOf(StageDependencyTable.Stage.ControlDependence).Basis);

        // 且它**不**在"早已执行完"的集合里——两个成因互斥。
        Assert.DoesNotContain(
          StageDependencyTable.Stage.ControlDependence,
          StageQuotaPolicy.StagesExecutedBeforePlanningPhase);
    }

    private static NLCPGBuilder Build(NLCPGCapability[] capabilities)
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = capabilities,
        });

        builder.BuildFromSource(Source, "stage-quota.cs");

        return builder;
    }
}
