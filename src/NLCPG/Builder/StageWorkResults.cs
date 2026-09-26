using NLCPG.Builder.Concurrency;

namespace NLCPG.Builder;

/// <summary>
/// G0-P **R-2**：为**尚无结果类型**的 4 个阶段补齐 <see cref="IStageWorkResult"/> 适配。
/// <para>
/// <b>适用范围（源码确证）：</b>`ControlFlow`、`ControlDependence`、`MemberAccess` 三者的
/// WorkBatch 执行结果**统一是 `LocalCpgFragment` 列表**
/// （`ControlFlowPass.cs:46`、`ControlDependencePass.cs:61`、`MemberAccessPass.cs:78`），
/// 故它们共用一个适配器；`InterproceduralDataFlow` 在 `NLCPGBuilder` 内直接发布桥接边，
/// 不产 fragment，另用计数结果。
/// </para>
/// <para>
/// ⚠ <b>纯记账：不改变任何执行路径、不改变任何产物。</b>这些类型只在阶段完成后用于记账。
/// </para>
/// </summary>
internal static class StageWorkResults
{
    /// <summary>
    /// fragment 型阶段的记账结果（`ControlFlow` / `ControlDependence` / `MemberAccess`）。
    /// <para>
    /// `ProducedNodeCount` 取**归并前**各 fragment 的 `Nodes.Count` 之和——
    /// 归并会去重，归并后的数量无法反映该阶段真实的分配压力（见接口注释）。
    /// </para>
    /// </summary>
    internal sealed record FragmentStageWorkResult(
      StageDependencyTable.Stage Stage,
      IReadOnlyList<LocalCpgFragment> Fragments) : IStageWorkResult
    {
        StageDependencyTable.Stage IStageWorkResult.Stage => Stage;

        long IStageWorkResult.ProducedNodeCount => ProducedNodeCount;

        /// <summary>归并前各 fragment 的节点数之和。</summary>
        internal long ProducedNodeCount { get; } =
          Fragments.Sum(fragment => (long)fragment.Nodes.Count);

        /// <summary>归并前各 fragment 的边数之和；用于记账核验。</summary>
        internal long ProducedEdgeCount { get; } =
          Fragments.Sum(fragment => (long)fragment.Edges.Count);
    }

    /// <summary>
    /// 事实计数型阶段的记账结果。
    /// <para>
    /// `MemberAccess` 的实际产出是 `MemberAccessFact`（**不是** `LocalCpgFragment`）——
    /// 实测于 `MemberAccessPass.cs:78-97`。故它不走 fragment 适配器。
    /// </para>
    /// </summary>
    internal sealed record FactCountStageWorkResult(
      StageDependencyTable.Stage Stage,
      long ProducedFactCount,
      long ProducedNodeCount) : IStageWorkResult
    {
        StageDependencyTable.Stage IStageWorkResult.Stage => Stage;

        long IStageWorkResult.ProducedNodeCount => ProducedNodeCount;
    }

    /// <summary>
    /// 跨过程发布段的记账结果。该阶段不产 fragment，而是在 `NLCPGBuilder` 内
    /// 直接从边的快照发布桥接边，故只记账条数。
    /// </summary>
    internal sealed record InterproceduralStageWorkResult(
      long ProducedNodeCount,
      long PublishedEdgeCount) : IStageWorkResult
    {
        StageDependencyTable.Stage IStageWorkResult.Stage =>
          StageDependencyTable.Stage.InterproceduralDataFlow;

        long IStageWorkResult.ProducedNodeCount => ProducedNodeCount;
    }
}
