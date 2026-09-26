using NLCPG.Builder.Concurrency;

namespace NLCPG.Builder;

/// <summary>
/// G0-P **R-3**：一个阶段的 plan 能在**何时**被算出来——把"能不能前移"变成**可机读的声明**。
/// <para>
/// <b>为什么需要它：</b>R.1 ① 的缺口是"规划发生在计算阶段内部 ⇒ 窗口开始前不知规模"。
/// 补齐该缺口时实测发现：**并非所有阶段都能前移**，而"我已前移"与"我前移不了"
/// 若只写在注释里，就无法被测试或消费者区分。本枚举让两者在类型上分开。
/// </para>
/// </summary>
internal enum StagePlanTiming
{
    /// <summary>
    /// 规划在**任何执行相位之前**完成：只依赖语法/语义模型、既有缓存与无状态的批次构造器。
    /// 该阶段的 plan 会出现于 <c>PlanSnapshotAtEndOfPlanningPhase</c>。
    /// </summary>
    StaticBeforeExecution,

    /// <summary>
    /// 规划依赖某个**前序阶段的运行期产物**，故只能在该前序阶段完成后进行。
    /// <para>
    /// 该阶段的 plan **不会**出现在 <c>PlanSnapshotAtEndOfPlanningPhase</c> 中——
    /// 这是**设计事实，不是缺陷**：它由 <c>StagePlan.RequiresRuntimeInputFrom</c> 指出来源。
    /// </para>
    /// </summary>
    DeferredUntilRuntimeInputs,
}

/// <summary>
/// G0-P **R-3**：阶段静态规划契约。
/// <para>
/// <b>为什么需要（附录 R.1 ①）：</b>此前每个 pass 在**自己的计算阶段内部**临时规划批次
/// （如 `ControlDependencePass.cs:40-60` 内联构造 `CpgWorkBatch`），
/// 导致**窗口开始前无法知道规模**，也就无法提前配额度（R.4 的前提）。
/// </para>
/// <para>
/// <b>本契约把规划提前为显式的、只读的一步：</b>
/// <list type="bullet">
/// <item><c>Plan*</c> —— **只读**：算出批次与预估，**不得**调用 <c>graph.AddEdge</c>/<c>AddNode</c>。</item>
/// <item><c>Commit*</c> —— **唯一写图者**：消费 plan，按 <c>StableOrder</c> 归并。</item>
/// </list>
/// </para>
/// <para>
/// ⚠ <b>本契约不改变任何产物</b>：拆分只是把既有的两段代码显式命名，
/// 批次构造参数逐字保持不变（见各阶段的 <c>Plan*</c> 实现）。
/// </para>
/// </summary>
/// <typeparam name="TBatch">该阶段的批次类型（通常为 <see cref="CpgWorkBatch"/>）。</typeparam>
internal interface IStagePlan<out TBatch>
{
    /// <summary>该 plan 所属阶段。</summary>
    StageDependencyTable.Stage Stage { get; }

    /// <summary>
    /// 本 plan 的规划时点（见 <see cref="StagePlanTiming"/>）。
    /// <para>⚠ 只描述**时点**，不表示该阶段已接入配额（那是 R-4）。</para>
    /// </summary>
    StagePlanTiming Timing { get; }

    /// <summary>
    /// 规划出的批次，按 <c>StableOrder</c> 升序。
    /// <para>只读产物：规划阶段不得修改图。</para>
    /// </summary>
    IReadOnlyList<TBatch> Batches { get; }

    /// <summary>
    /// 规划期预估的节点总数——供 R.4 的**窗口前**准入预检使用。
    /// <para>
    /// ⚠ 这是**规划期估算**（来自批次的 <c>EstimatedNodeCount</c>），**不是**实际产出；
    /// 实际产出由 R-2 的 <see cref="IStageWorkResult"/> 在提交后回报。
    /// </para>
    /// </summary>
    long EstimatedNodeCount { get; }

    /// <summary>
    /// 规划期预估的字节总量（对应设计 R.2 ① 的 <c>PlannedBytes</c>）。
    /// <para>
    /// ⚠ <b>这是估算，不是上界，也不是额度依据。</b>附录 G 已实测
    /// <c>Estimate(startLine, endLine)</c> 的自变量**只有行号**，
    /// 同一行范围下真实载荷跨度达 1349×，故**不存在**可用常量使其成为上界。
    /// R-4 只用它**留证**（记录规划时刻的估算值），不用它分配额度。
    /// </para>
    /// </summary>
    long EstimatedBytes { get; }
}

/// <summary>
/// G0-P **R-3**：批次型阶段的 plan 实现。
/// </summary>
/// <param name="Stage">阶段标识。</param>
/// <param name="Batches">按 <c>StableOrder</c> 升序的批次。</param>
/// <param name="Timing">
/// 规划时点。默认 <see cref="StagePlanTiming.StaticBeforeExecution"/>，
/// 因为能被提前规划的阶段是常态；**延迟规划的阶段必须显式声明**，
/// 否则"没能前移"会被默认值静默伪装成"已前移"。
/// </param>
/// <param name="RequiresRuntimeInputFrom">
/// 当 <paramref name="Timing"/> 为 <see cref="StagePlanTiming.DeferredUntilRuntimeInputs"/> 时，
/// 指出该 plan 依赖的**前序阶段**；否则必须为 <c>null</c>。
/// </param>
internal sealed record StagePlan(
  StageDependencyTable.Stage Stage,
  IReadOnlyList<CpgWorkBatch> Batches,
  StagePlanTiming Timing = StagePlanTiming.StaticBeforeExecution,
  StageDependencyTable.Stage? RequiresRuntimeInputFrom = null) : IStagePlan<CpgWorkBatch>
{
    /// <inheritdoc />
    public long EstimatedNodeCount { get; } =
      Batches.Sum(batch => (long)batch.EstimatedNodeCount);

    /// <summary>规划期预估的字节总量；供 R.4 的窗口前预检。</summary>
    public long EstimatedBytes { get; } =
      Batches.Sum(batch => (long)batch.EstimatedBytes);
    /// <summary>规划期预估的条目总数。</summary>
    public long EstimatedItemCount { get; } =
      Batches.Sum(batch => (long)batch.Items.Count);

    /// <summary>
    /// 该 plan 是否**确实**在任何执行相位之前就已算出。
    /// <para>
    /// 供消费者与契约测试判定时点，而不必解析 <see cref="Timing"/> 的枚举字面量。
    /// </para>
    /// </summary>
    internal bool WasPlannedBeforeExecution =>
      Timing == StagePlanTiming.StaticBeforeExecution;

    /// <summary>
    /// 自校验：时点与"运行期输入来源"必须**互相一致**——
    /// 静态可前移的 plan 不得声称依赖运行期产物，反之亦然。
    /// <para>
    /// ⚠ 这是一条**内部一致性**检查，不证明前移真的发生过；
    /// 后者由 builder 的规划相位快照判定。
    /// </para>
    /// </summary>
    internal bool IsSelfConsistent => Timing switch
    {
        StagePlanTiming.StaticBeforeExecution => RequiresRuntimeInputFrom is null,
        StagePlanTiming.DeferredUntilRuntimeInputs => RequiresRuntimeInputFrom is not null,
        _ => false,
    };
}
