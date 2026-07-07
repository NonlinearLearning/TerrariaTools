using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Model;

namespace RoslynPrototype.Analysis;

/// <summary>
/// 结构分析阶段共享的只读上下文。
/// </summary>
public sealed record CpgAnalysisContext(
  /// <summary>
  /// 当前源码对应的主 CPG 图。
  /// </summary>
  RoslynCpgGraph Graph,
  /// <summary>
  /// 当前源码的 Roslyn 语义模型。
  /// </summary>
  SemanticModel SemanticModel,
  /// <summary>
  /// 当前编译单元的语法树根节点。
  /// </summary>
  SyntaxNode CompilationRoot);
