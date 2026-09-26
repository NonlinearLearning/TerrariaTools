namespace NLISSN.Application;

/// <summary>
/// 单个方法的**最小输入描述符**：计划器只需要「稳定方法序号」与「成本」两项，
/// 不需要 Roslyn 语法/语义对象。
/// <para>
/// <see cref="MethodOrder"/> 与 <c>GetOperationRootPlans</c>
/// （src\NLCPG\Builder\Passes\OperationPass.cs:69-120）为每个操作根分配的 <c>order</c> 同源，
/// 即 <see cref="DocumentShard.MethodOrders"/> 中承载的值；它只在**单个文件内**唯一。
/// </para>
/// <para>
/// <see cref="EstimatedCost"/> 是方法体行数 <c>endLine - startLine + 1</c>，
/// 与 <c>CpgWorkBatchCostModel.Estimate</c> 的币种一致，**不得**另立一套口径。
/// </para>
/// </summary>
/// <param name="MethodOrder">文件内稳定的方法（操作根）序号，必须非负且文件内唯一。</param>
/// <param name="EstimatedCost">方法体行数，必须不小于 1。</param>
public sealed record DocumentMethodDescriptor(
  int MethodOrder,
  int EstimatedCost);
