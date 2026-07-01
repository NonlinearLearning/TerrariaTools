using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 结构分析阶段共享的只读上下文。
/// </summary>
public sealed record CpgAnalysisContext(
  RoslynCpgGraph Graph,
  SemanticModel SemanticModel,
  SyntaxNode CompilationRoot);
