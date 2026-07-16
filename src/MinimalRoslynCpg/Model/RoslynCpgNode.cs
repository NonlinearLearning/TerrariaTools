using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 表示最小 Roslyn CPG 中的一个节点。
/// </summary>
public sealed record RoslynCpgNode(
  RoslynCpgNodeKind Kind,      // 节点类别枚举，给程序分支判断用
  string DisplayKind,          // 面向展示/输出的节点类别文本
  string? Name = null,         // 简短名称，如变量名、方法名、类型名
  string? FullName = null,     // 全限定名，如命名空间+类型+成员签名
  string? Signature = null,    // 签名信息，常用于方法/调用节点
  RoslynCpgDispatchKind? DispatchKind = null, // 结构化分派/动作标签
  string? TypeFullName = null, // 节点对应的数据类型全名
  string? FilePath = null,     // 所属源文件路径
  int? SpanStart = null,       // 源码起始位置
  int? SpanEnd = null,         // 源码结束位置
  string? Text = null,         // 节点关联的源码文本或摘要文本
  bool IsImplicit = false,     // 是否为 Roslyn 隐式生成的语义节点
  NodeId? NodeId = null,       // 冻结图主身份；构图阶段可暂未分配
  StableNodeAnchor? StableAnchor = null); // 冻结图 NodeId 分配输入
