using Microsoft.CodeAnalysis;
using NLCPG.Model;

namespace NLISSN.Core.Marking;

/// 表示规则在标记阶段产出的一条直接命中记录。
public sealed record MarkRecord(
  /// 产生这条标记的规则标识。
  string RuleId,
  /// 规则命中的语法节点。
  SyntaxNode SyntaxNode,
  /// 为后续传播、决策或改写绑定到语法树上的注解。
  SyntaxAnnotation? Annotation,
  /// 与当前语法节点对齐的主图节点。
  NLCPGNode? PrimaryGraphNode,
  /// 说明本次命中的原因，供调试和结果输出使用。
  string Reason,
  /// 阶段之间共享的规则分组键；为空时回退到 RuleId。
  string? GroupKey = null);
