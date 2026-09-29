using NLCPG.Builder;
using NLCPG.Contracts;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// G0-P **R-3** 的验证：阶段规划是**只读的**、**时点前移到执行之前**的、**可观测的**。
/// <para>
/// <b>为什么必须测"时点"而不只是"存在"：</b>把 <c>Plan*</c> 写成 <c>Commit*</c> 的同义词
/// （即仍在原处被调用、只是换了函数名）**完全可以通过"plan 存在"这类断言**，
/// 但那样规划时点**根本没前移**，R.4 的配额预检也就无从谈起。
/// 故本文件的核心用例断言的是**规划先于执行**这一时序事实。
/// </para>
/// </summary>
public sealed class StagePlanContractTests
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
    /// 含**调用与属性访问**的输入。
    /// <para>
    /// ⚠ 上面的 <see cref="Source"/> 只有赋值与返回，**没有**任何 invocation/property reference，
    /// 而 <c>CallGraph</c> 的 plan 恰以这两类操作为输入——用那个输入去断言 CallGraph
    /// 只会得到"plan 为 null"，从而把"阶段未被规划"误判成"已前移"。故此处单列一个输入。
    /// </para>
    /// </summary>
    private const string CallSiteSource = """
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
                var bumped = counter.Bump(2);
                counter.Value = bumped;
                return counter.Value;
            }
        }
        """;

    /// <summary>`ControlFlow` 在执行前必须已有 plan，且 plan 的批次数 > 0。</summary>
    [Fact]
    public void BuildFromSource_WhenCfgRequested_ControlFlowPlanExistsBeforePassesRun()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        var plans = builder.LastStagePlans;

        var plan = Assert.IsAssignableFrom<IStagePlan<NLCPG.Builder.Concurrency.CpgWorkBatch>>(
          Assert.Contains(StageDependencyTable.Stage.ControlFlow, plans));

        Assert.Equal(StageDependencyTable.Stage.ControlFlow, plan.Stage);
        Assert.NotEmpty(plan.Batches);
    }

    /// <summary>
    /// **核心用例：规划必须先于执行。**
    /// <para>
    /// 判据来自 builder 暴露的**记录顺序**：`LastStagePlans` 在规划相位的开头被填充，
    /// 而 `LastStageWorkResults`（R-2 记于执行之后）此时必为空——
    /// 若两者同时非空，说明规划被挪进了执行相位（正是"只改名、没前移"的形态）。
    /// </para>
    /// <para>
    /// ⚠ 这里用 builder 暴露的真实记录做判据，不读源码文本
    /// （附录 N.4 的教训：读源码文本的断言会与并发修改相互干扰）。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenCfgRequested_PlanningPhaseCompletesBeforeAnyWorkResultIsRecorded()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        // 构建完成后：plan 与 result 都在。
        Assert.NotEmpty(builder.LastStagePlans);
        Assert.NotEmpty(builder.LastStageWorkResults);

        // 而规划相位结束时 result 必为空——由 builder 记录的规划相位快照证明。
        Assert.NotNull(builder.PlanSnapshotAtEndOfPlanningPhase);
        Assert.NotEmpty(builder.PlanSnapshotAtEndOfPlanningPhase!);

        Assert.Empty(
          builder.WorkResultsAtEndOfPlanningPhase
            ?? throw new InvalidOperationException("未记录规划相位结束时的结果快照。"));

        // 且规划相位已经包含了 ControlFlow 的 plan。
        Assert.Contains(
          StageDependencyTable.Stage.ControlFlow,
          builder.PlanSnapshotAtEndOfPlanningPhase!);
    }

    /// <summary>
    /// plan 的预估值必须与实际批次一致——防止 plan 记的是"另一份算出来的数"。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenCfgRequested_PlanEstimatesMatchItsOwnBatches()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        var plan = (StagePlan)builder.LastStagePlans[StageDependencyTable.Stage.ControlFlow];

        Assert.Equal(
          plan.Batches.Sum(batch => (long)batch.EstimatedNodeCount),
          plan.EstimatedNodeCount);

        Assert.Equal(
          plan.Batches.Sum(batch => (long)batch.EstimatedBytes),
          plan.EstimatedBytes);

        Assert.Equal(
          plan.Batches.Sum(batch => (long)batch.Items.Count),
          plan.EstimatedItemCount);
    }

    /// <summary>
    /// 阶段**只能规划一次**：重复登记必须抛出。
    /// <para>
    /// 这条守住"规划必须在执行前完成且只做一次"——若某阶段既在规划相位登记、
    /// 又在执行相位重新规划，就会触发。
    /// </para>
    /// <para>
    /// ⚠ 此处刻意用 <c>DeferredUntilRuntimeInputs</c>：本测试直接调用 builder、
    /// 不在规划相位内，故用静态时点会先撞上"声明与实际不符"的时点校验，
    /// 从而**测不到**重复登记这条路径（这正是本用例首版的真实失败原因）。
    /// </para>
    /// </summary>
    [Fact]
    public void RecordStagePlan_WhenSameStageRecordedTwice_Throws()
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.Cfg },
        });

        var plan = new StagePlan(
          StageDependencyTable.Stage.ControlDependence,
          Array.Empty<NLCPG.Builder.Concurrency.CpgWorkBatch>(),
          StagePlanTiming.DeferredUntilRuntimeInputs,
          RequiresRuntimeInputFrom: StageDependencyTable.Stage.Dominance);

        builder.RecordStagePlan(plan);

        var exception = Assert.Throws<InvalidOperationException>(
          () => builder.RecordStagePlan(plan));

        Assert.Contains("规划了多次", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// **核心用例：本轮前移的阶段必须全部出现在规划相位快照中。**
    /// <para>
    /// <b>为什么按阶段逐个断言、而不是只断言"快照非空"：</b>"快照非空"在只有
    /// ControlFlow 一个阶段前移时同样成立（那正是更早一轮的状态）。
    /// 逐阶段断言才能把"新增阶段确实前移了"与"早先已前移的那些"区分开——
    /// 否则本用例对"某阶段只改了名没前移"毫无鉴别力。
    /// </para>
    /// <para>
    /// ⚠ 这里断言 4 个阶段（CallGraph/ControlFlow/MemberAccess/Dominance），
    /// 它们**不包含** DataFlow：后者需要单独请求 <c>DataFlow</c> 能力，
    /// 其前移由 <see cref="BuildFromSource_WhenDataFlowRequested_IsPlannedBeforeExecution"/> 覆盖。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenAllPlannableCapabilitiesRequested_AllFourStagesPlannedBeforeExecution()
    {
        var builder = Build(
          new[]
          {
              NLCPGCapability.CallTargets,
              NLCPGCapability.Cfg,
              NLCPGCapability.MethodModel,
              NLCPGCapability.Dominance,
          },
          CallSiteSource);

        var snapshot = builder.PlanSnapshotAtEndOfPlanningPhase
          ?? throw new InvalidOperationException("未记录规划相位结束时的 plan 快照。");

        Assert.Contains(StageDependencyTable.Stage.CallGraph, snapshot);
        Assert.Contains(StageDependencyTable.Stage.ControlFlow, snapshot);
        Assert.Contains(StageDependencyTable.Stage.MemberAccess, snapshot);
        Assert.Contains(StageDependencyTable.Stage.Dominance, snapshot);

        // 四个 plan 都必须非空——否则"出现在快照里"可能只是登记了一个空壳。
        foreach (var stage in new[]
                 {
                     StageDependencyTable.Stage.CallGraph,
                     StageDependencyTable.Stage.ControlFlow,
                     StageDependencyTable.Stage.MemberAccess,
                     StageDependencyTable.Stage.Dominance,
                 })
        {
            Assert.NotEmpty(builder.LastStagePlans[stage].Batches);
        }
    }

    /// <summary>
    /// 前移的阶段都必须**声明**自己的时点为 <c>StaticBeforeExecution</c>。
    /// <para>
    /// 这条防的是"时点靠注释、枚举随便填"：若某阶段实际在执行相位规划，
    /// 它就会出现在 <c>LastStagePlans</c> 却不该出现在快照中，
    /// 而 <c>RecordStagePlan</c> 的 fail-closed 校验会直接抛异常（见下一个用例）。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenStaticallyPlanned_PlanDeclaresStaticTimingWithNoRuntimeDependency()
    {
        var builder = Build(
          new[]
          {
              NLCPGCapability.CallTargets,
              NLCPGCapability.Cfg,
              NLCPGCapability.MethodModel,
              NLCPGCapability.Dominance,
          },
          CallSiteSource);

        foreach (var stage in new[]
                 {
                     StageDependencyTable.Stage.CallGraph,
                     StageDependencyTable.Stage.ControlFlow,
                     StageDependencyTable.Stage.MemberAccess,
                     StageDependencyTable.Stage.Dominance,
                 })
        {
            var plan = (StagePlan)builder.LastStagePlans[stage];

            Assert.Equal(StagePlanTiming.StaticBeforeExecution, plan.Timing);
            Assert.Null(plan.RequiresRuntimeInputFrom);
            Assert.True(plan.IsSelfConsistent);
            Assert.True(plan.WasPlannedBeforeExecution);
        }
    }

    /// <summary>
    /// **ControlDependence 的诚实性用例：它必须如实声明为"延迟规划"。**
    /// <para>
    /// 该阶段依赖 Dominance **运行期**填充的 <c>_dominanceOverlays</c>，
    /// 故它的 plan 必然**缺席**规划相位快照——这是设计事实。
    /// 本用例同时断言"缺席"与"如实声明延迟"，把该事实钉成机制：
    /// 若有人日后把它的 <c>Timing</c> 改成 Static（谎称已前移），这里立刻失败。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlDependenceRequested_IsDeferredAndAbsentFromPlanningSnapshot()
    {
        var builder = Build(new[] { NLCPGCapability.ControlDependence });

        var snapshot = builder.PlanSnapshotAtEndOfPlanningPhase
          ?? throw new InvalidOperationException("未记录规划相位结束时的 plan 快照。");

        // 它**不在**规划相位快照里……
        Assert.DoesNotContain(StageDependencyTable.Stage.ControlDependence, snapshot);

        // ……但在执行后确实被规划并登记了，且**如实**声明为延迟。
        var plan = (StagePlan)builder.LastStagePlans[StageDependencyTable.Stage.ControlDependence];
        Assert.Equal(StagePlanTiming.DeferredUntilRuntimeInputs, plan.Timing);
        Assert.Equal(StageDependencyTable.Stage.Dominance, plan.RequiresRuntimeInputFrom);
        Assert.True(plan.IsSelfConsistent);
        Assert.False(plan.WasPlannedBeforeExecution);
    }

    /// <summary>
    /// **fail-closed：声明与实际时点不符必须抛出。**
    /// <para>
    /// 这条是 R-3 从"注释约定"升级为"机制"的关键：builder 处于<b>规划相位之外</b>时，
    /// 任何声称 <c>StaticBeforeExecution</c> 的登记都会立刻失败。
    /// 否则"我改了名但没前移"可以一路绿灯通过全部既有断言。
    /// </para>
    /// </summary>
    [Fact]
    public void RecordStagePlan_WhenStaticTimingRecordedOutsidePlanningPhase_Throws()
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.Cfg },
        });

        // 全新 builder 尚未进入规划相位，却声称"我已静态前移" ⇒ 必须抛。
        var plan = new StagePlan(
          StageDependencyTable.Stage.ControlFlow,
          Array.Empty<NLCPG.Builder.Concurrency.CpgWorkBatch>());

        var exception = Assert.Throws<InvalidOperationException>(() => builder.RecordStagePlan(plan));

        Assert.Contains("规划时点声明与实际不符", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// **fail-closed：窗口外依赖的来源必须真是权威表中的前置。**
    /// <para>
    /// <b>为什么需要这条：</b><c>RequiresRuntimeInputFrom</c> 此前是**只写不读**的——
    /// 它把"本阶段依赖 Dominance 的运行期产物"这一事实**又声明了一遍**，
    /// 而 <see cref="StageDependencyTable"/> 里已有一条同样的登记，两者**互不校验**。
    /// 于是计划里可以点名一个**根本不是自己前置**的阶段（凭空造依赖），无人发现。
    /// </para>
    /// <para>
    /// 本用例故意让 <c>ControlDependence</c> 声称依赖 <c>CallGraph</c>——
    /// 后者**确实是**已登记阶段，但**不是** <c>ControlDependence</c> 的前置
    /// （权威表登记的是 <c>Dominance</c>）。这个"名字合法但关系不合法"的形态
    /// 正是只看"名字是否存在"的校验抓不住的。
    /// </para>
    /// </summary>
    [Fact]
    public void RecordStagePlan_WhenRuntimeSourceIsNotADeclaredPredecessor_Throws()
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.ControlDependence },
        });

        // ControlDependence 的权威前置是 Dominance；此处谎报 CallGraph。
        var plan = new StagePlan(
          StageDependencyTable.Stage.ControlDependence,
          Array.Empty<NLCPG.Builder.Concurrency.CpgWorkBatch>(),
          StagePlanTiming.DeferredUntilRuntimeInputs,
          RequiresRuntimeInputFrom: StageDependencyTable.Stage.CallGraph);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.RecordStagePlan(plan));

        Assert.Contains("未把", exception.Message, StringComparison.Ordinal);
        Assert.Contains("G0-P ④", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// **该谎报不会误伤合法声明**——`Dominance` 是权威表登记的真正前置，必须放行。
    /// <para>
    /// 与上一条**成对**：只测"非法被拒"无法排除"所有延迟声明都被拒"。
    /// 这一对把判据从"能抛异常"收紧到"**按关系**判定"。
    /// </para>
    /// </summary>
    [Fact]
    public void RecordStagePlan_WhenRuntimeSourceIsTheDeclaredPredecessor_IsAccepted()
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.ControlDependence },
        });

        var plan = new StagePlan(
          StageDependencyTable.Stage.ControlDependence,
          Array.Empty<NLCPG.Builder.Concurrency.CpgWorkBatch>(),
          StagePlanTiming.DeferredUntilRuntimeInputs,
          RequiresRuntimeInputFrom: StageDependencyTable.Stage.Dominance);

        builder.RecordStagePlan(plan);

        Assert.Contains(
          StageDependencyTable.Stage.ControlDependence,
          builder.LastStagePlans.Keys);
    }

    /// <summary>
    /// **真实构建中，延迟规划的来源阶段确实已经跑过**——即 `Dominance` 先于
    /// `ControlDependence` 的 plan 登记。
    /// <para>
    /// <b>这条覆盖的是执行相位内那条"来源必须已执行"的守卫</b>：
    /// 它若被触发，说明某次重排让延迟规划跑在了来源之前——
    /// 而那正是附录 Q.4 实测过的"静默少边"形态（该阶段整体静默 return）。
    /// 正常构建必须通过，故本用例断言的是"不抛 + plan 如实登记"。
    /// </para>
    /// <para>
    /// ⚠ 该守卫**只在执行相位内**判定（见 <c>RecordStagePlan</c> 内注释）：
    /// 构建尚未开始时不存在执行历史，断言"来源未执行"会把"无从判定"误报成"确实违反"。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlDependenceRequested_ItsRuntimeSourceRanFirst()
    {
        var builder = Build(new[] { NLCPGCapability.ControlDependence });

        // 构建成功即说明守卫未误报；再确认延迟 plan 如实登记了来源。
        var plan = (StagePlan)builder.LastStagePlans[StageDependencyTable.Stage.ControlDependence];

        Assert.Equal(StagePlanTiming.DeferredUntilRuntimeInputs, plan.Timing);
        Assert.Equal(StageDependencyTable.Stage.Dominance, plan.RequiresRuntimeInputFrom);
        // 来源确实是权威表登记的**前置**，两处声明一致。
        Assert.Contains(
          StageDependencyTable.Stage.Dominance,
          StageDependencyTable.PredecessorsOf(StageDependencyTable.Stage.ControlDependence));
    }

    /// <summary>
    /// **fail-closed：延迟规划点名的来源必须在本**次**构建中真的执行过。**
    /// <para>
    /// <b>为什么这条不是空转：</b>只做"来源是不是登记过的前置"（上一条）还不够——
    /// 那验证的是**静态关系**，而 <c>RequiresRuntimeInputFrom</c> 声称的是
    /// **本次构建的运行期事实**。一个 plan 完全可以"点名一个合法的前置"，
    /// 却在一次**根本没跑过该前置**的构建里被登记。
    /// </para>
    /// <para>
    /// 本用例构造的正是该形态：先用只请求 <c>Cfg</c> 的构建跑完一轮
    /// （执行序列只有 <c>ControlFlow</c>，**没有** <c>Dominance</c>），
    /// 再往同一个 builder 登记一个声称依赖 <c>Dominance</c> 的延迟 plan。
    /// 此时规划相位已闭合、执行历史里没有 <c>Dominance</c> ⇒ 必须拒绝。
    /// </para>
    /// <para>
    /// ⚠ 该守卫**只在规划相位闭合后**判定（构建尚未开始时不存在执行历史，
    /// 断言"未执行"会把"无从判定"误报成"确实违反"）。
    /// </para>
    /// </summary>
    [Fact]
    public void RecordStagePlan_WhenRuntimeSourceDidNotRunInThisBuild_Throws()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        // 本轮构建确实没有执行 Dominance。
        var plan = new StagePlan(
          StageDependencyTable.Stage.ControlDependence,
          Array.Empty<NLCPG.Builder.Concurrency.CpgWorkBatch>(),
          StagePlanTiming.DeferredUntilRuntimeInputs,
          RequiresRuntimeInputFrom: StageDependencyTable.Stage.Dominance);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.RecordStagePlan(plan));

        Assert.Contains("尚未执行", exception.Message, StringComparison.Ordinal);
        Assert.Contains("G0-P ④", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// **该守卫不误伤正常构建**——请求 `ControlDependence` 时能力闭包会把
    /// `Dominance` 一并带上（<c>NLCPGBuilder</c> 的能力解析），故来源必然已执行。
    /// <para>
    /// 与上一条**成对**：只测"未执行被拒"无法排除"守卫把所有延迟登记都拒了"。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenControlDependenceRequested_DominanceRunsBeforeIt()
    {
        var builder = Build(new[] { NLCPGCapability.ControlDependence });

        // 能构建成功、且延迟 plan 如实登记 ⇒ 守卫未误报。
        var plan = (StagePlan)builder.LastStagePlans[StageDependencyTable.Stage.ControlDependence];
        Assert.Equal(StageDependencyTable.Stage.Dominance, plan.RequiresRuntimeInputFrom);
    }

    /// <summary>
    /// **fail-closed：自相矛盾的声明必须抛出。**
    /// <para>
    /// 声称静态可前移、却又说依赖运行期产物，二者不能同时成立。
    /// </para>
    /// </summary>
    [Fact]
    public void RecordStagePlan_WhenStaticTimingClaimsRuntimeDependency_Throws()
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.Cfg },
        });

        var plan = new StagePlan(
          StageDependencyTable.Stage.ControlFlow,
          Array.Empty<NLCPG.Builder.Concurrency.CpgWorkBatch>(),
          StagePlanTiming.StaticBeforeExecution,
          RequiresRuntimeInputFrom: StageDependencyTable.Stage.Dominance);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.RecordStagePlan(plan));

        Assert.Contains("声明自相矛盾", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 未请求 `Cfg` 时不得留下 `ControlFlow` 的 plan——规划相位必须尊重能力位。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenCfgNotRequested_DoesNotRecordControlFlowPlan()
    {
        var builder = Build(new[] { NLCPGCapability.MethodModel });

        Assert.DoesNotContain(StageDependencyTable.Stage.ControlFlow, builder.LastStagePlans.Keys);
    }

    /// <summary>
    /// **DataFlow 必须出现在规划相位快照中（轮次 25 前移）。**
    /// <para>
    /// <b>为什么这条用例有鉴别力：</b>轮次 25 之前 <c>DataFlow</c> 的规划留在执行相位内，
    /// 故它出现在 <c>LastStagePlans</c> 却**不**出现在 <c>PlanSnapshotAtEndOfPlanningPhase</c>。
    /// 只断言"plan 存在"对两种状态都成立 ⇒ 必须断言**时点**。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowRequested_IsPlannedBeforeExecution()
    {
        var builder = Build(new[] { NLCPGCapability.DataFlow });

        var snapshot = builder.PlanSnapshotAtEndOfPlanningPhase
          ?? throw new InvalidOperationException("未记录规划相位结束时的 plan 快照。");

        Assert.Contains(StageDependencyTable.Stage.DataFlow, snapshot);

        var plan = (StagePlan)builder.LastStagePlans[StageDependencyTable.Stage.DataFlow];
        Assert.Equal(StagePlanTiming.StaticBeforeExecution, plan.Timing);
        Assert.Null(plan.RequiresRuntimeInputFrom);
        Assert.True(plan.WasPlannedBeforeExecution);
        Assert.NotEmpty(plan.Batches);
    }

    /// <summary>
    /// DataFlow 前移后，规划相位快照必须包含**全部 5 个**可批次规划的后置阶段。
    /// <para>
    /// 这条把"本轮新增 DataFlow"与"上一轮的 4 个"区分开：
    /// 上一轮状态下本用例会因缺少 <c>DataFlow</c> 而失败。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenAllBatchStagesRequested_FiveStagesPlannedBeforeExecution()
    {
        var builder = Build(
          new[]
          {
              NLCPGCapability.CallTargets,
              NLCPGCapability.Cfg,
              NLCPGCapability.MethodModel,
              NLCPGCapability.Dominance,
              NLCPGCapability.DataFlow,
          },
          CallSiteSource);

        var snapshot = builder.PlanSnapshotAtEndOfPlanningPhase
          ?? throw new InvalidOperationException("未记录规划相位结束时的 plan 快照。");

        foreach (var stage in new[]
                 {
                     StageDependencyTable.Stage.CallGraph,
                     StageDependencyTable.Stage.ControlFlow,
                     StageDependencyTable.Stage.MemberAccess,
                     StageDependencyTable.Stage.Dominance,
                     StageDependencyTable.Stage.DataFlow,
                 })
        {
            Assert.Contains(stage, snapshot);
            Assert.NotEmpty(builder.LastStagePlans[stage].Batches);
        }
    }

    /// <summary>
    /// **DataFlow 的批次必须由 plan 携带，而不是提交时重算。**
    /// <para>
    /// 判据：<c>DataFlowBatchCount</c>（提交时消费 <c>plan.Batches</c> 得到）
    /// 必须与 <c>plan.Batches.Count</c> **相等**。
    /// 若提交步无视 plan、自己重算批次，两者在正常路径上仍会相等——
    /// 故本用例**单独不足以**证明"没有重算"，它证明的是"plan 的规模与实际执行规模一致"
    /// （即 plan 没有被算成另一份数）。真正的"未重算"由变异②证明（见附录 X.5）。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowRequested_BatchCountMatchesThePlan()
    {
        var builder = Build(new[] { NLCPGCapability.DataFlow });

        var plan = (StagePlan)builder.LastStagePlans[StageDependencyTable.Stage.DataFlow];

        Assert.Equal(plan.Batches.Count, builder.LastBuildMetrics.DataFlowBatchCount);
        // 工作线程数来自执行器本身，与 plan 无关；断言 > 0 即可（具体值由夹具的 DOP 决定）。
        Assert.True(builder.LastBuildMetrics.DataFlowWorkerCount > 0);
    }

    /// <summary>
    /// 未请求 `DataFlow` 时不得留下它的 plan——前移不得绕过能力位。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowNotRequested_DoesNotRecordDataFlowPlan()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        Assert.DoesNotContain(StageDependencyTable.Stage.DataFlow, builder.LastStagePlans.Keys);
    }

    /// <summary>
    /// 规划相位**必须尊重能力位**：未请求的阶段不得被提前规划。
    /// <para>
    /// 与上一条的区别：这里显式请求了一半、留空另一半，
    /// 从而排除"规划相位无差别地把所有阶段都算一遍"这种错误实现。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenCallTargetsNotRequested_DoesNotRecordCallGraphPlan()
    {
        // 只请求 Cfg（会连带 MethodModel），但**不**请求 CallTargets/Dominance。
        var builder = Build(new[] { NLCPGCapability.Cfg });

        var snapshot = builder.PlanSnapshotAtEndOfPlanningPhase
          ?? throw new InvalidOperationException("未记录规划相位结束时的 plan 快照。");

        Assert.Contains(StageDependencyTable.Stage.ControlFlow, snapshot);
        Assert.Contains(StageDependencyTable.Stage.MemberAccess, snapshot);
        Assert.DoesNotContain(StageDependencyTable.Stage.CallGraph, snapshot);
        Assert.DoesNotContain(StageDependencyTable.Stage.Dominance, snapshot);
    }

    /// <summary>
    /// **DataFlow 的批次必须"只算一次"——由规划相位算，提交步只消费。**
    /// <para>
    /// <b>为什么必须用调用计数、而不能用行为断言：</b>若提交步无视 plan、自己再调一次
    /// <c>AssembleDataFlowWorkBatches</c>，产物与"消费 plan"**逐字等价**
    /// （同一纯函数、同一份分区输入）。这一点已实测：该"重算"变异在全部
    /// <b>197</b> 条 DataFlow/StagePlan 相关用例下**全部通过**（附录 X.5 变异②）。
    /// 故只能以"构造次数"为判据，把"每阶段只规划一次"这一**既有不变量**
    /// （<c>RecordStagePlan</c> 对重复登记抛异常）延伸到批次构造上。
    /// </para>
    /// <para>
    /// <c>0</c> 表示规划相位没算（= 未前移或未请求），<c>2</c> 表示提交步又算了一遍。
    /// 只有 <c>1</c> 才说明"规划算、提交用"。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowRequested_AssemblesBatchesExactlyOnce()
    {
        var builder = Build(new[] { NLCPGCapability.DataFlow });

        Assert.Equal(1, builder.DataFlowPlanAssemblyCount);
    }

    /// <summary>
    /// 未请求 `DataFlow` 时**一次都不该算**——计数必须真的跟随请求，
    /// 否则上一条用例的"恒为 1"可能只是常量吻合。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenDataFlowNotRequested_AssemblesNoBatches()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg });

        Assert.Equal(0, builder.DataFlowPlanAssemblyCount);
    }

    /// <summary>
    /// **CallGraph 的方法索引必须在 worker 启动前冻结**（方案 1 + 2 的留证）。
    /// <para>
    /// <b>为什么必须用计数、而不能用行为断言：</b>冻结只消除候选集的**读取时刻依赖**，
    /// 不改变候选集内容——"冻结了"与"整个改动都不存在"产出的图**逐字等价**
    /// （同 <see cref="BuildFromSource_WhenDataFlowRequested_AssemblesBatchesExactlyOnce"/>
    /// 的 X.5 变异问题）。故只能以"预注册次数"为判据。
    /// </para>
    /// <para>
    /// 这里用含调用**与**属性访问的 <see cref="CallSiteSource"/>：两条路径各自都要预注册，
    /// 只覆盖其一会让计数偏小却仍然非零，故下面同时断言它确实覆盖了访问器路径。
    /// </para>
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenCallTargetsRequested_FreezesMethodIndexWithNonEmptyCount()
    {
        var builder = Build(new[] { NLCPGCapability.CallTargets }, CallSiteSource);

        // 调用路径与访问器路径都会贡献，故必然远大于 1。
        Assert.True(
          builder.CallGraphFrozenMethodIndexCount > 1,
          $"预期冻结次数 > 1（调用 + 访问器两条路径），实际 {builder.CallGraphFrozenMethodIndexCount}。");
    }

    /// <summary>
    /// 未请求 `CallTargets` 时**不该冻结**——计数必须真的跟随请求，
    /// 否则上一条的"非零"可能只是常量吻合。
    /// </summary>
    [Fact]
    public void BuildFromSource_WhenCallTargetsNotRequested_DoesNotFreezeMethodIndex()
    {
        var builder = Build(new[] { NLCPGCapability.Cfg }, CallSiteSource);

        Assert.Equal(0, builder.CallGraphFrozenMethodIndexCount);
    }

    private static NLCPGBuilder Build(NLCPGCapability[] capabilities, string? source = null)
    {
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = capabilities,
        });

        builder.BuildFromSource(source ?? Source, "stage-plan.cs");

        return builder;
    }
}
