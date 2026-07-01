using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Model;

namespace RoslynPrototype.Marking;

/// <summary>
/// 表示规则在标记阶段产出的一条直接命中记录。
/// </summary>
public sealed record MarkRecord(
  /// <summary>
  /// 产生这条标记的规则标识。
  /// </summary>
  string RuleId,
  /// <summary>
  /// 规则命中的语法节点。
  /// </summary>
  SyntaxNode SyntaxNode,
  /// <summary>
  /// 为后续传播、决策或改写绑定到语法树上的注解。
  /// </summary>
  SyntaxAnnotation? Annotation,
  /// <summary>
  /// 与当前语法节点对齐的主图节点。
  /// </summary>
  RoslynCpgNode? PrimaryGraphNode,
  /// <summary>
  /// 说明本次命中的原因，供调试和结果输出使用。
  /// </summary>
  string Reason);
