namespace NLCPG.Builder.Passes;

/// 跨过程桥计划的【惰性载体】（⑥ 延迟物化）：只记"这条计划用的是池里哪个序号"。
///
/// 前身是 216 B 的 <c>InterproceduralDataFlowPlan</c>——它内嵌两个 <c>NLCPGNode</c>（各 104 B）
/// 与 <c>BridgeKind</c>/<c>ArgumentOrdinal</c>。端点节点值与桥种类都是【池内已有事实】的副本，
/// 而池在本 pass 内建成后只读（`NLCPGBuilder.cs` 的"本段不调用 AddEdge/AddNode"），
/// 故序号在"创建 → 回读"之间恒不变，副本没有存在必要。
///
/// 思路来源是 Roslyn 的红/绿树：宽的不可变事实（绿树）只有一份，
/// 中间结果持窄引用（红树），真正需要时才展开。
///
/// 槽位分工（宽度恰 8 B = 两个 int，按 4 B 对齐）：
///   · 第 1 个 int <see cref="PoolOrdinal"/>：唯一的端点来源，也是"不物化"的句柄；
///   · 第 2 个 int <see cref="ArgumentOrdinal"/>：排序的第 2 键，构造期由
///     <c>ParseArgumentOrdinal</c> 一次性落定。存下来可让 <c>static</c> 的
///     <c>BuildAndSortPlanRows</c> 不必访问实例字典 `_methodParameterOrdinalsByNode`。
///
/// ⚠ 排序第 1 键 <c>BridgeKind</c> 刻意【不存】——它由池内边的端点种类唯一导出
/// （见 `NLCPGBuilder.BridgeKindOf`）。这是本类型唯一的隐式耦合，护栏是
/// `InterproceduralEdgeIndexSnapshotTests` 里那条"导出与三段产生者一致"的 Fact。
///
/// ⚠ 池本身绝不能被改成序号——那才是把"值"换成"句柄"，会触碰 M2 的池快照契约。
/// 本类型只改池的【消费者】。
internal readonly record struct InterproceduralPlanRef(
  int PoolOrdinal,
  int ArgumentOrdinal = -1);
