namespace NLCPG.Builder;

/// <summary>
/// G0-P **R-4**：配额**作用域**分级（附录 R 四要素第 ③ 项）。
/// <para>
/// ⚠ <b>本类型管的是"作用域"，不是"大小"</b>——二者常被混为一谈，故在此写清：
/// 作用域回答"这个阶段用哪个池"，大小回答"给它多少字节"。
/// 附录 G 已**实测证明**后者不存在可用的公式上界
/// （<c>Estimate</c> 的自变量只有行号，同一行范围下真实载荷跨度达 <b>1349×</b>），
/// 故本轮**不**推导任何字节额度——那会是凭空造数，正是计划 :114 点名禁止的形态。
/// </para>
/// </summary>
internal enum StageQuotaScope
{
    /// <summary>与同层其他阶段共享池。**这是默认值**，也是当前生产的真实行为。</summary>
    Shared,

    /// <summary>
    /// 独占额度。**启用前必须先有实测标定**（见 <see cref="StageQuotaCalibration"/>），
    /// 否则 <see cref="StageQuotaPolicy.Resolve"/> 会抛出，而不是静默降级。
    /// </summary>
    Dedicated,
}

/// <summary>
/// G0-P **R-4**：某阶段的**实测标定**——启用 <see cref="StageQuotaScope.Dedicated"/> 的唯一凭据。
/// <para>
/// <b>为什么需要它：</b>附录 R.2 ③ 写明 <c>Dedicated</c>「需实测标定后才可启用」。
/// 若"标定"只写在文档里，那么把某阶段改成 <c>Dedicated</c> 是一个**不可被拒绝的操作**——
/// 谁都能改，且没有任何东西能指出"你还没标定"。本记录把该前提变成**必要的入参**。
/// </para>
/// </summary>
/// <param name="Stage">被标定的阶段。</param>
/// <param name="MeasuredPeakBytes">实测峰值字节数（必须为正；不允许用估算值冒充）。</param>
/// <param name="Evidence">证据出处（夹具名/日志路径/快照时间），供复核。</param>
internal sealed record StageQuotaCalibration(
  StageDependencyTable.Stage Stage,
  long MeasuredPeakBytes,
  string Evidence);

/// <summary>
/// G0-P **R-4**：某阶段的配额作用域**授予**结果。
/// <para>
/// 刻意区分 <see cref="RequestedScope"/> 与 <see cref="GrantedScope"/>：
/// "某阶段想要独占"与"它拿到了独占"是两件事，合并成一个字段就无法表达拒绝。
/// </para>
/// </summary>
/// <param name="Stage">阶段标识。</param>
/// <param name="RequestedScope">策略中声明的请求作用域。</param>
/// <param name="GrantedScope">
/// 实际授予的作用域。⚠ <b>当请求被拒绝时本字段不会被写到</b>——<see cref="StageQuotaPolicy.Resolve"/>
/// 会**抛出**（见 <c>EnsureCalibrated</c>），而不是降级为 <see cref="StageQuotaScope.Shared"/>：
/// 静默降级会让"我给它配了独占额度"与"它其实还在共享池里"在行为上无法区分。
/// 故当前生产配置（全 <c>Shared</c>）下本字段恒等于 <see cref="RequestedScope"/>。
/// </param>
/// <param name="Basis">该阶段能否在窗口开始前知道规模的**依据分类**。</param>
/// <param name="EstimatedBytesAtPlanningTime">
/// 规划时刻的字节估算；未在规划相位产出 plan 时为 <c>null</c>。
/// <para>⚠ 仅作留证，**不**作为额度依据（见 <see cref="StageQuotaScope"/> 的说明）。</para>
/// </param>
internal sealed record StageQuotaGrant(
  StageDependencyTable.Stage Stage,
  StageQuotaScope RequestedScope,
  StageQuotaScope GrantedScope,
  StageQuotaBasis Basis,
  long? EstimatedBytesAtPlanningTime)
{
    /// <summary>该阶段是否真的拿到了独占额度。</summary>
    internal bool IsDedicated => GrantedScope == StageQuotaScope.Dedicated;

    /// <summary>
    /// 该阶段的规模是否**在窗口开始前就已可知**——这是配额作用域分级的前提。
    /// </summary>
    internal bool ScaleWasKnownBeforeExecution => Basis == StageQuotaBasis.PlannedBeforeExecution;
}

/// <summary>
/// G0-P **R-4**：某阶段的 plan 能否在配额决策中被使用——把"缺席"的**成因**分类。
/// <para>
/// <b>为什么必须有这个分类：</b>附录 N.4/P.4/S.2 反复踩到同一形态——
/// 观测面上的"空"有多个成因，若不区分就会把一个成因的结论套到另一个上。
/// 这里三个值对应三种**互不相同**的成因，任何一种都不等于"该阶段已前移但规模为 0"。
/// </para>
/// </summary>
internal enum StageQuotaBasis
{
    /// <summary>规划相位已产出 plan ⇒ 窗口开始前已知规模，可据以决定作用域。</summary>
    PlannedBeforeExecution,

    /// <summary>
    /// 规划依赖**前序阶段的运行期产物**（如 <c>ControlDependence</c> 需要
    /// <c>Dominance</c> 填充的 <c>_dominanceOverlays</c>）⇒ 窗口开始前规模未知。
    /// </summary>
    DeferredPlanning,

    /// <summary>
    /// 该阶段**没有批次型 plan**（`InterproceduralDataFlow` 是纯转发，不走批次执行器）
    /// ⇒ R-3/R-4 对它是 **N/A**，而非"待办"。
    /// </summary>
    NoBatchPlan,

    /// <summary>
    /// 该阶段在**规划相位开始之前就已经执行完毕**（`Syntax` / `Operation`）。
    /// <para>
    /// <b>为何必须与 <see cref="DeferredPlanning"/> 分开：</b>两者都"没有 plan"，
    /// 但成因**相反**——<c>DeferredPlanning</c> 是"时候未到"（规模还未知，将来会知道），
    /// 而本项是"时候已过"（该阶段**已经跑完**，plan 的概念对它不适用）。
    /// 若合并，"已执行完的阶段"会被描述成"窗口开始前规模未知"——那是**与事实相反**的陈述。
    /// </para>
    /// <para>
    /// 实测依据：<c>NLCPGBuilder.cs</c> 中 <c>RunSyntaxPass</c> 与
    /// <c>RunPartitionedOperationPass</c> 都位于规划相位开启
    /// （<c>_planningPhaseState = PlanningPhaseState.Open</c>）**之前**。
    /// </para>
    /// <para>
    /// ⚠ <b>刻意不写行号：</b>本轮实测该处的行号已随相邻改动漂移过至少两次
    /// （<c>:374</c>/<c>:401</c>/<c>:452</c> → 现为 <c>:393</c>/<c>:421</c>/<c>:479</c>），
    /// 而**行号失效是静默的**——读者无法分辨"引对了"与"引到了别的行"。
    /// 判据是**顺序关系**（两处调用在相位开启之前），故按可搜索的符号引用。
    /// </para>
    /// <para>
    /// 这正是规划相位的作用域被定义为"先于**后置 pass 阶段**"而非"先于任何 worker"的原因
    /// （附录 V.2）。
    /// </para>
    /// </summary>
    ExecutedBeforePlanningPhase,
}

/// <summary>
/// G0-P **R-4**：配额作用域策略——把"该阶段用哪个池"从散落的注释变成**唯一权威声明 + 可校验解析**。
/// </summary>
internal static class StageQuotaPolicy
{
    /// <summary>
    /// 策略声明的**请求**作用域。**默认全部 <see cref="StageQuotaScope.Shared"/>**。
    /// <para>
    /// 这是刻意的：附录 R.2 ③ 要求"不得凭空引入新额度"，而默认值正是这条要求的落点。
    /// 把某阶段改成 <c>Dedicated</c> 必须**同时**提供实测标定，否则
    /// <see cref="Resolve"/> 会抛出（见 <see cref="EnsureCalibrated"/>）。
    /// </para>
    /// <para>
    /// <b>候选（本轮未启用）：</b><c>InterproceduralDataFlow</c> 的桥接发布段
    /// 实测同时存活 <b>755.3 MiB</b>（占当时存活堆 67.75%，<c>NLCPGBuilder.cs:1440</c>），
    /// 与局部 pass 不在同一量级 ⇒ 它是 <c>Dedicated</c> 的**候选**。
    /// 但它**同时**属于 <see cref="StageQuotaBasis.NoBatchPlan"/>（无批次型 plan），
    /// 故本轮既不能也不需要为它配作用域。
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<StageDependencyTable.Stage, StageQuotaScope> RequestedScopes =
      StageDependencyTable.AllStages.ToDictionary(
        stage => stage,
        _ => StageQuotaScope.Shared);

    /// <summary>查询某阶段在策略中声明的请求作用域。</summary>
    internal static StageQuotaScope RequestedScopeOf(StageDependencyTable.Stage stage)
    {
        return RequestedScopes.TryGetValue(stage, out var scope) ? scope : StageQuotaScope.Shared;
    }

    /// <summary>
    /// 哪些阶段**存在批次型 plan**（即走 <c>_workBatchExecutor</c>）。
    /// <para>
    /// 这是 <see cref="StageQuotaBasis.NoBatchPlan"/> 判定的唯一依据，
    /// 逐条由源码确证（每个阶段的 <c>_workBatchExecutor.*</c> 调用点）：
    /// </para>
    /// <list type="bullet">
    /// <item><c>Syntax</c> —— <c>PartitionedSyntaxPass.cs:112</c></item>
    /// <item><c>Operation</c> —— <c>PartitionedOperationPass.cs:158</c></item>
    /// <item><c>CallGraph</c> —— <c>CallGraphPass.cs:107</c></item>
    /// <item><c>MemberAccess</c> —— <c>MemberAccessPass.cs:127</c></item>
    /// <item><c>ControlFlow</c> —— <c>ControlFlowPass.cs:80</c></item>
    /// <item><c>DataFlow</c> —— <c>DataFlowPass.cs:618</c></item>
    /// <item><c>Dominance</c> —— <c>DominancePass.cs:345</c></item>
    /// <item><c>ControlDependence</c> —— <c>ControlDependencePass.cs:107</c></item>
    /// </list>
    /// <para>
    /// ⚠ <b>刻意缺席：<c>InterproceduralDataFlow</c></b>——它是
    /// <c>builder.RunInterproceduralDataFlowPass</c> 的纯转发，**不走批次执行器**，
    /// 故 R-3/R-4 对它是 <b>N/A</b> 而非待办。
    /// </para>
    /// </summary>
    internal static IReadOnlySet<StageDependencyTable.Stage> BatchPlanCapableStages { get; } =
      new HashSet<StageDependencyTable.Stage>
      {
          StageDependencyTable.Stage.Syntax,
          StageDependencyTable.Stage.Operation,
          StageDependencyTable.Stage.CallGraph,
          StageDependencyTable.Stage.MemberAccess,
          StageDependencyTable.Stage.ControlFlow,
          StageDependencyTable.Stage.DataFlow,
          StageDependencyTable.Stage.Dominance,
          StageDependencyTable.Stage.ControlDependence,
      };

    /// <summary>
    /// **在规划相位开启之前**就已执行完的阶段——它们既不会有、也不需要 plan。
    /// <para>
    /// 实测依据（<c>NLCPGBuilder.cs</c>）：<c>RunSyntaxPass</c> 与
    /// <c>RunPartitionedOperationPass</c> 都早于规划相位开启
    /// （<c>_planningPhaseState = PlanningPhaseState.Open</c>）。
    /// ⚠ 与上文同样**刻意不写行号**（理由见 <see cref="StageQuotaBasis.ExecutedBeforePlanningPhase"/>）。
    /// 这也解释了规划相位的作用域为何是"先于**后置 pass 阶段**"而非"先于任何 worker"（附录 V.2）。
    /// </para>
    /// <para>
    /// ⚠ <b>它们同时在 <see cref="BatchPlanCapableStages"/> 里</b>（确实走批次执行器），
    /// 故若不以本集合单独排除，就会被 <see cref="Resolve"/> 归入
    /// <see cref="StageQuotaBasis.DeferredPlanning"/>——即被描述成"窗口开始前规模未知"，
    /// **与事实相反**：它们早已跑完。两个集合**都**是必需的。
    /// </para>
    /// </summary>
    internal static IReadOnlySet<StageDependencyTable.Stage> StagesExecutedBeforePlanningPhase { get; } =
      new HashSet<StageDependencyTable.Stage>
      {
          StageDependencyTable.Stage.Syntax,
          StageDependencyTable.Stage.Operation,
      };

    /// <summary>
    /// 批次执行器 <c>stageId</c> 前缀；与 <c>CpgWorkBatchPerformanceStageId</c> 的 8 个字面量同源
    /// （每个都是 <c>前缀 + Stage 成员名</c>）。
    /// </summary>
    internal const string WorkBatchStageIdPrefix = "CPG.WorkBatch.";

    /// <summary>
    /// 把执行期观测到的 <c>stageId</c> 解析回 <see cref="StageDependencyTable.Stage"/>。
    /// <para>
    /// <b>刻意由后缀反解，而不是再写一张"id → 阶段"的映射表：</b>两张表必然漂移，
    /// 而漂移后对账会**静默失效**（对不上就跳过）。反解只有一份真值来源。
    /// </para>
    /// <para>
    /// 返回 <c>false</c> 表示"这不是阶段 id"（例如测试自造的契约 id）——调用方必须
    /// **跳过**而非报警：执行器是通用组件，<c>stageId</c> 对它只是标签，
    /// 把非阶段标签当违规会把测试与未来调用方全部误报。
    /// </para>
    /// </summary>
    internal static bool TryResolveDeclaredStage(string? stageId, out StageDependencyTable.Stage stage)
    {
        stage = default;
        if (string.IsNullOrEmpty(stageId)
            || !stageId.StartsWith(WorkBatchStageIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = stageId.AsSpan(WorkBatchStageIdPrefix.Length);
        foreach (var candidate in StageDependencyTable.AllStages)
        {
            if (suffix.SequenceEqual(candidate.ToString()))
            {
                stage = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// **fail-closed 对账：声明表 vs 执行期观测。**
    /// <para>
    /// <see cref="BatchPlanCapableStages"/> 是一张**手写声明**："这 8 个阶段走批次执行器"。
    /// 在加入本方法之前，它**没有任何消费者对账**——若某阶段被漏写，它会被
    /// <see cref="Resolve"/> 归入 <see cref="StageQuotaBasis.NoBatchPlan"/>，
    /// 即"R-3/R-4 对它是 N/A"，从而**静默豁免**整套配额作用域治理
    /// （含未标定 <c>Dedicated</c> 的拒绝守卫）。这与附录 AA 的形态同源：声明在，机制不在。
    /// </para>
    /// <para>
    /// 判据是**结构层**的（附录 X.5/Y.4）：<c>NoBatchPlan</c> 断言"本阶段不提交批次"，
    /// 而执行器此刻正拿着它的批次——两者不可同时为真。故不看行为结果，直接比对分类。
    /// </para>
    /// <para>
    /// ⚠ <b>两类阶段必须放行，否则会产生误报：</b>
    /// </para>
    /// <list type="number">
    /// <item>解析前的阶段（<c>_stageQuotaAllocation</c> 仍为 <c>Empty</c>）——
    /// <c>Syntax</c>/<c>Operation</c> 在规划相位之前就执行，那时**配额表尚未存在**，
    /// 没有可对账的声明。这也正是规划相位作用域被定义为"先于后置 pass 阶段"的原因（附录 V.2）。</item>
    /// <item>解析后归入其他成因的阶段——<c>ExecutedBeforePlanningPhase</c> 是"时候已过"、
    /// <c>DeferredPlanning</c> 是"时候未到"，两者都**确实**走批次执行器，
    /// 只有 <c>NoBatchPlan</c> 才声称"不走"。</item>
    /// </list>
    /// </summary>
    /// <param name="allocation">本次构建已解析的配额表；<c>Empty</c> 表示规划相位尚未解析。</param>
    /// <param name="stageId">执行器收到的阶段标签。</param>
    internal static void ReconcileObservedStageExecution(StageQuotaAllocation allocation, string? stageId)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (!TryResolveDeclaredStage(stageId, out var stage))
        {
            return;
        }

        if (allocation.Grants.Count == 0)
        {
            return;
        }

        if (!allocation.TryGrantOf(stage, out var grant) || grant.Basis != StageQuotaBasis.NoBatchPlan)
        {
            return;
        }

        throw new InvalidOperationException(
          $"G0-P R-4 声明表与执行期观测不符：阶段 {stage} 在配额表中被声明为 "
          + $"{StageQuotaBasis.NoBatchPlan}（\"没有批次型 plan\" ⇒ R-3/R-4 对它是 N/A），"
          + $"但它的批次正在通过批次执行器执行（stageId=\"{stageId}\"）。"
          + $"要么它应被登记进 {nameof(BatchPlanCapableStages)}，要么它的批次提交点应被移除——"
          + "两者都不做即为\"声明代替机制\"（附录 AA）。");
    }

    /// <summary>
    /// **消费规划相位**产出配额作用域授予表。
    /// <para>
    /// <b>这是 R-4 的关键一步，也是"规划相位尚未被消费"的关闭点：</b>
    /// 输入是**规划相位的快照**（<paramref name="plansAtEndOfPlanningPhase"/>），
    /// 而不是任何独立重算的来源。某个阶段在本表中被判为
    /// <see cref="StageQuotaBasis.PlannedBeforeExecution"/>，**当且仅当**它确实出现在该快照里。
    /// 故"规划真的前移了"与"配额据此分级了"由同一个事实驱动，不可能各自漂移。
    /// </para>
    /// </summary>
    /// <param name="plansAtEndOfPlanningPhase">
    /// 规划相位结束瞬间已登记的阶段 → plan。形状与
    /// <c>NLCPGBuilder.PlanSnapshotAtEndOfPlanningPhase</c> 一致。
    /// </param>
    /// <param name="stageBatchPlanCapable">
    /// 该阶段是否存在**批次型** plan（即是否走批次执行器）。
    /// </param>
    /// <param name="stagesExecutedBeforePlanningPhase">
    /// 在规划相位开启**之前**就已执行完的阶段。
    /// <para>
    /// ⚠ 这不是可选装饰：缺了它，这些阶段会因"没有 plan"而被归入
    /// <see cref="StageQuotaBasis.DeferredPlanning"/>，即被描述为"窗口开始前规模未知"——
    /// 而它们其实**早已跑完**，成因恰好相反。两者的差别不是措辞问题：
    /// 前者意味着"将来会前移"，后者意味着"R-3 对它不适用"。
    /// </para>
    /// </param>
    /// <param name="calibrations">已完成的实测标定；无标定的 <c>Dedicated</c> 请求会被拒绝。</param>
    /// <param name="requestedScopes">
    /// 覆盖策略默认的请求作用域表。
    /// <para>
    /// ⚠ <b>为可测性而存在，且必须如此：</b>生产策略本轮全为 <c>Shared</c>，
    /// 意味着"未标定的 <c>Dedicated</c> 被拒绝"这条守卫在生产配置下**永远不会被走到**。
    /// 若不为它留一个入口，该守卫就只能靠读源码确认——而本仓库的既有教训
    /// （附录 N.4/P.4）正是"未被观测面覆盖的断言等于不存在"。
    /// 传 <c>null</c> 即用策略默认表（生产路径）。
    /// </para>
    /// </param>
    internal static StageQuotaAllocation Resolve(
      IReadOnlyDictionary<StageDependencyTable.Stage, IStagePlan<Concurrency.CpgWorkBatch>> plansAtEndOfPlanningPhase,
      IReadOnlySet<StageDependencyTable.Stage> stageBatchPlanCapable,
      IReadOnlyList<StageQuotaCalibration>? calibrations = null,
      IReadOnlyDictionary<StageDependencyTable.Stage, StageQuotaScope>? requestedScopes = null,
      IReadOnlySet<StageDependencyTable.Stage>? stagesExecutedBeforePlanningPhase = null)
    {
        ArgumentNullException.ThrowIfNull(plansAtEndOfPlanningPhase);
        ArgumentNullException.ThrowIfNull(stageBatchPlanCapable);

        var grants = new List<StageQuotaGrant>(StageDependencyTable.AllStages.Count);
        foreach (var stage in StageDependencyTable.AllStages)
        {
            var requested = requestedScopes is not null && requestedScopes.TryGetValue(stage, out var overridden)
              ? overridden
              : RequestedScopeOf(stage);
            var hasPlan = plansAtEndOfPlanningPhase.TryGetValue(stage, out var plan);
            // 成因分类（三选一，互斥）：
            //   有 plan                        ⇒ 规划相位已产出，规模在窗口前可知；
            //   早已执行完（Syntax/Operation）  ⇒ plan 概念不适用（时候已过）；
            //   其余批次型阶段                  ⇒ 规划被推迟（时候未到）。
            // 后两者都"没有 plan"却成因相反，故必须分开登记。
            var basis = hasPlan
              ? StageQuotaBasis.PlannedBeforeExecution
              : stagesExecutedBeforePlanningPhase?.Contains(stage) == true
                ? StageQuotaBasis.ExecutedBeforePlanningPhase
                : stageBatchPlanCapable.Contains(stage)
                  ? StageQuotaBasis.DeferredPlanning
                  : StageQuotaBasis.NoBatchPlan;

            // Dedicated 的唯一凭据是实测标定；缺失即拒绝，不静默降级。
            if (requested == StageQuotaScope.Dedicated)
            {
                EnsureCalibrated(stage, basis, calibrations);
            }

            grants.Add(new StageQuotaGrant(
              stage,
              requested,
              // 本轮所有请求都是 Shared，故授予等同请求；Dedicated 路径由 EnsureCalibrated 把关。
              requested,
              basis,
              hasPlan ? plan!.EstimatedBytes : null));
        }

        return new StageQuotaAllocation(grants);
    }

    /// <summary>
    /// <b>fail-closed：</b>把某阶段声明为 <c>Dedicated</c> 时，必须有其实测标定。
    /// <para>
    /// 刻意**不**静默降级为 <c>Shared</c>：静默降级会让"我给它配了独占额度"与
    /// "它其实还在共享池里"在行为上无法区分，正是本仓库反复出现的失效形态。
    /// 抛出才能让未经标定的启用**不可发生**。
    /// </para>
    /// </summary>
    private static void EnsureCalibrated(
      StageDependencyTable.Stage stage,
      StageQuotaBasis basis,
      IReadOnlyList<StageQuotaCalibration>? calibrations)
    {
        if (basis != StageQuotaBasis.PlannedBeforeExecution)
        {
            throw new InvalidOperationException(
              $"阶段 {stage} 被声明为 {StageQuotaScope.Dedicated}，但它的规划依据是 {basis}——"
              + "窗口开始前规模未知（或无批次型 plan），无从据以配额度（G0-P R-4）。"
              + "请先使其在规划相位产出 plan，或保持 Shared。");
        }

        var calibration = calibrations?.FirstOrDefault(item => item.Stage == stage);
        if (calibration is null)
        {
            throw new InvalidOperationException(
              $"阶段 {stage} 被声明为 {StageQuotaScope.Dedicated}，但没有实测标定（G0-P R-4）。"
              + "附录 R.2 ③ 要求 Dedicated 必须【先有实测标定】；"
              + "附录 G 已实测不存在可用的 bytes 公式上界，故不得用估算值代替标定。");
        }

        if (calibration.MeasuredPeakBytes <= 0)
        {
            throw new InvalidOperationException(
              $"阶段 {stage} 的实测标定值必须为正，实际为 {calibration.MeasuredPeakBytes}（G0-P R-4）。");
        }

        if (string.IsNullOrWhiteSpace(calibration.Evidence))
        {
            throw new InvalidOperationException(
              $"阶段 {stage} 的实测标定缺少证据出处（G0-P R-4）；无出处的标定无法复核，等同于没有标定。");
        }
    }
}

/// <summary>
/// G0-P **R-4**：一次构建的**配额作用域授予表**——由规划相位快照解析得出。
/// <para>
/// 与 <c>LastStagePlans</c> 一样是**只读结果**，不是配置开关；
/// 它**不改变**任何执行行为（本轮无 <c>Dedicated</c> 启用 ⇒ 全部 <c>Shared</c>，
/// 与既有单池行为逐字一致）。
/// </para>
/// </summary>
internal sealed class StageQuotaAllocation
{
    internal StageQuotaAllocation(IReadOnlyList<StageQuotaGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        Grants = grants;
        ByStage = grants.ToDictionary(grant => grant.Stage);
    }

    internal static StageQuotaAllocation Empty { get; } = new(Array.Empty<StageQuotaGrant>());

    /// <summary>全部阶段的授予记录，按 <see cref="StageDependencyTable.AllStages"/> 声明序。</summary>
    internal IReadOnlyList<StageQuotaGrant> Grants { get; }

    private IReadOnlyDictionary<StageDependencyTable.Stage, StageQuotaGrant> ByStage { get; }

    internal StageQuotaGrant GrantOf(StageDependencyTable.Stage stage) => ByStage[stage];

    /// <summary>
    /// 非抛出版本：用于执行期对账——对账要处理"表里根本没有该阶段"的形态，
    /// 而不是让它以 <see cref="KeyNotFoundException"/> 的形式逃逸（那会掩盖真正的违规）。
    /// </summary>
    internal bool TryGrantOf(StageDependencyTable.Stage stage, out StageQuotaGrant grant)
    {
        return ByStage.TryGetValue(stage, out grant!);
    }

    /// <summary>拿到了独占额度的阶段（本轮应为空——无标定 ⇒ 无 Dedicated）。</summary>
    internal IReadOnlyList<StageDependencyTable.Stage> DedicatedStages =>
      Grants.Where(grant => grant.IsDedicated).Select(grant => grant.Stage).ToArray();

    /// <summary>规模在**窗口开始前**就已可知的阶段——即规划相位真正覆盖到的那些。</summary>
    internal IReadOnlyList<StageDependencyTable.Stage> ScaleKnownBeforeExecution =>
      Grants.Where(grant => grant.ScaleWasKnownBeforeExecution).Select(grant => grant.Stage).ToArray();
}
