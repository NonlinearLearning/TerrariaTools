namespace NLISSN.Application;

/// <summary>
/// 大文件方法集合切分后的**一个分片**。
/// <para>
/// ⚠ <b>原子单位是完整函数</b>：分片之间的边界**只**能落在方法与方法之间，
/// <see cref="MethodOrders"/> 是分片负责的方法（操作根）序号全集，
/// 内部**绝不**再按基本块或语句切开。
/// </para>
/// <para>
/// <see cref="EstimatedCost"/> 与 <see cref="EstimatedBytes"/> 是**估算**，不是上界：
/// 币种与方法体行数一致（见 <see cref="DocumentMethodDescriptor.EstimatedCost"/>），
/// 换算常量见 <see cref="DocumentShardPlanner.EstimatedBytesPerCostUnit"/>。
/// </para>
/// </summary>
/// <param name="StableOrder">
/// **全局单调、跨文件唯一**的调度序号。由 <see cref="DocumentShardPlanner"/> 按
/// 「文件序号 × 步长 + 片内序号」确定性导出（见 <see cref="DocumentShardPlanner.AssignFileOrdinals"/>）。
/// </param>
/// <param name="ShardIndex">文件**内**的分片序号，从 0 起连续。</param>
/// <param name="MethodOrders">该分片负责的方法序号，父序升序；每个方法在整份计划里恰好出现一次。</param>
/// <param name="EstimatedCost">分片内全部方法体行数之和；**允许**大于目标片行数（该方法自身超标时）。</param>
/// <param name="EstimatedBytes">按 <c>EstimatedCost × 64</c> 折算的估算字节数。</param>
public sealed record DocumentShard(
  long StableOrder,
  int ShardIndex,
  IReadOnlyList<int> MethodOrders,
  int EstimatedCost,
  long EstimatedBytes);
