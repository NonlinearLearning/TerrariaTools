using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Rules;

public sealed record IndexerParameterShrinkPlan(
  IndexerDeclarationSyntax Indexer,
  IndexerDeclarationSyntax ReplacementIndexer,
  IReadOnlyList<ElementAccessRewrite> AccessRewrites);
