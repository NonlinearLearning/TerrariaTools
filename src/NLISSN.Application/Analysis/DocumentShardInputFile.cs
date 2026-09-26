namespace NLISSN.Application;

/// <summary>
/// 计划器的一个输入文件：路径 + 已抽取好的方法描述符列表。
/// <para>
/// 该记录让「跨文件」这一层可以确定性排序：<see cref="DocumentShardPlanner.PlanAll"/>
/// 按 <see cref="FilePath"/> 的序数序分配文件序号，从而得到跨文件唯一的
/// <see cref="DocumentShard.StableOrder"/>。
/// </para>
/// </summary>
/// <param name="FilePath">文件路径；仅作标签与跨文件身份，不读文件内容。</param>
/// <param name="Methods">该文件的方法描述符；顺序无关紧要，计划器内部会归一化排序。</param>
public sealed record DocumentShardInputFile(
  string FilePath,
  IReadOnlyList<DocumentMethodDescriptor> Methods);
