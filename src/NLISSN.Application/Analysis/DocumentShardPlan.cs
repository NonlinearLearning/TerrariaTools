namespace NLISSN.Application;

/// <summary>
/// 单个源文件的**分片计划**：把该文件的方法集合切成 K 个标准大小的分片。
/// <para>
/// ⚠ <b>不要把它读成「消除超标方法」的承诺。</b>「按完整函数切分」这一硬约束使得
/// 「方法自身大于目标片行数」不可消除：此类方法独占一片并**允许超出目标**。
/// 本计划的承诺只是「在保持函数完整的前提下尽量贴近目标」。
/// </para>
/// </summary>
/// <param name="FilePath">被规划的文件路径，仅作标签与跨文件身份使用。</param>
/// <param name="SizeClass">按方法体总行数得到的文件大小档；非 <see cref="FileSizeClass.Large"/> 时只有 1 片。</param>
/// <param name="Shards">分片列表，按 <see cref="DocumentShard.ShardIndex"/> 升序。</param>
/// <param name="EstimatedBytes">全部 <see cref="DocumentShard.EstimatedBytes"/> 之和。</param>
/// <remarks>
/// ⚠ <b>记录相等性陷阱</b>：<see cref="Shards"/> 是集合成员，
/// <c>record</c> 生成的 <c>Equals</c> 对它只做**引用比较**。
/// 两份内容相同但分别构造的计划<b>不</b>相等，比较计划请逐字段投影（或先比 <see cref="Shards"/> 元素）。
/// </remarks>
public sealed record DocumentShardPlan(
  string FilePath,
  FileSizeClass SizeClass,
  IReadOnlyList<DocumentShard> Shards,
  long EstimatedBytes);
