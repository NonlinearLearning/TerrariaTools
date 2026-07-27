using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

/// 索引器参数删除与所有受影响元素访问替换的同步计划。
public sealed record IndexerParameterShrinkPlan(
  IndexerDeclarationSyntax Indexer,
  IndexerDeclarationSyntax ReplacementIndexer,
  IReadOnlyList<ElementAccessRewrite> AccessRewrites);
