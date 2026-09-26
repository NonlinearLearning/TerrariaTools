namespace NLCPG.Builder;

/// <summary>
/// G0-P **R-2**：阶段工作结果的**公共契约**。
/// <para>
/// <b>为什么需要它（附录 R.1 ②）：</b>此前 5 个阶段各有**自己的私有嵌套 record**
/// （`SyntaxWorkBatchResult`、`OperationWorkBatchResult`、`CallGraphWorkBatchResult`、
/// `DataFlowWorkBatchResult`、`DominanceWorkBatchResult`），**彼此无公共契约**；
/// 而 `ControlFlow`/`ControlDependence`/`MemberAccess`/`InterproceduralDataFlow`
/// **连结果类型都没有**。⇒ 无法在窗口层统一记账，也无法支撑 R.4 的配额作用域。
/// </para>
/// <para>
/// <b>本接口只做三件事：</b>回答「属于哪个阶段」「产出多少节点」「归并顺序」。
/// 刻意**不**抽象出统一的结果载荷——各阶段的载荷形态差异真实存在
/// （`SyntaxPartitionResult` / `CallGraphFact` / `DominanceRootResult` / `LocalCpgFragment`），
/// 强行统一会引入无意义的包装层。
/// </para>
/// <para>
/// ⚠ <b>本类型是纯记账契约：不改变任何既有数据结构、不改变任何执行顺序、不改变任何产物。</b>
/// </para>
/// </summary>
internal interface IStageWorkResult
{
    /// <summary>该结果所属阶段（与 <see cref="StageDependencyTable.Stage"/> 同一标识空间）。</summary>
    StageDependencyTable.Stage Stage { get; }

    /// <summary>
    /// 归并**前**该批产出的节点数——供 R-4 的配额回填与窗口层记账使用。
    /// <para>
    /// 取「归并前」是因为归并会去重，归并后的数量无法反映该阶段真实的分配压力。
    /// </para>
    /// </summary>
    long ProducedNodeCount { get; }
}
