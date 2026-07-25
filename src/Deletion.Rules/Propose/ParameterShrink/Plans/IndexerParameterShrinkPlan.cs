using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Deletion.Rules;

public sealed record IndexerParameterShrinkPlan(
  IndexerDeclarationSyntax Indexer,
  IndexerDeclarationSyntax ReplacementIndexer,
  IReadOnlyList<ElementAccessRewrite> AccessRewrites);
