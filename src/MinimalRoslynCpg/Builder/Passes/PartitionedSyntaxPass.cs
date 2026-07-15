using Microsoft.CodeAnalysis;

namespace MinimalRoslynCpg.Builder;

public sealed partial class RoslynCpgBuilder
{
  private sealed record SyntaxSemanticFacts(
    ISymbol? DeclaredSymbol,
    bool QueriedDeclaredSymbol,
    ISymbol? ReferencedSymbol,
    SyntaxTypeResolution TypeResolution,
    bool ShouldDeferToOperation);

  private void RunSyntaxPass(
    RoslynCpgBuildContext context,
    bool usePartitionedSyntaxPass,
    IReadOnlyList<OperationRootPlan> operationRoots)
  {
    if (!usePartitionedSyntaxPass)
    {
      RunLegacySyntaxPass(context);
      return;
    }

    var partitionRoots = operationRoots
      .Select(root => root.BodySyntax)
      .Where(root => !operationRoots.Any(other =>
        !ReferenceEquals(root, other.BodySyntax) && root.Ancestors().Contains(other.BodySyntax)))
      .ToArray();
    var partitions = partitionRoots
      .Select(root => root.DescendantNodesAndSelf().ToArray())
      .ToArray();
    var partitionSyntax = new HashSet<SyntaxNode>(
      partitions.SelectMany(nodes => nodes),
      ReferenceEqualityComparer.Instance);
    foreach (var syntax in context.Root.DescendantNodesAndSelf().Where(node => !partitionSyntax.Contains(node)))
    {
      _partitionedSyntaxFacts[syntax] = AnalyzeSyntaxFacts(syntax, context.SemanticModel);
    }

    var results = RunSyntaxPartitionsAsync(partitions, context.SemanticModel).GetAwaiter().GetResult();
    foreach (var facts in results)
    {
      foreach (var entry in facts)
      {
        _partitionedSyntaxFacts[entry.Key] = entry.Value;
      }
    }

    RunPartitionedSyntaxPass(context, partitionRoots, partitions);
    _syntaxPassTelemetry = _syntaxPassTelemetry with
    {
      SyntaxPartitionCount = partitions.Length,
      SyntaxPartitionMaxDegreeOfParallelism = _options.EffectiveMaxDegreeOfParallelism,
    };
    _partitionedSyntaxFacts.Clear();
  }

  private async Task<IReadOnlyList<IReadOnlyDictionary<SyntaxNode, SyntaxSemanticFacts>>> RunSyntaxPartitionsAsync(
    IReadOnlyList<SyntaxNode[]> partitions,
    SemanticModel semanticModel)
  {
    return await BoundedPartitionWorkWindow.RunAsync(
      partitions,
      _options.EffectiveMaxDegreeOfParallelism,
      (partition, _) => AnalyzeSyntaxPartition(partition, semanticModel));
  }

  private IReadOnlyDictionary<SyntaxNode, SyntaxSemanticFacts> AnalyzeSyntaxPartition(
    IReadOnlyList<SyntaxNode> syntaxNodes,
    SemanticModel semanticModel)
  {
    var facts = new Dictionary<SyntaxNode, SyntaxSemanticFacts>(ReferenceEqualityComparer.Instance);
    foreach (var syntax in syntaxNodes)
    {
      facts[syntax] = AnalyzeSyntaxFacts(syntax, semanticModel);
    }

    return facts;
  }

  private SyntaxSemanticFacts AnalyzeSyntaxFacts(SyntaxNode syntax, SemanticModel semanticModel)
  {
    var referencedSymbol = CanReferenceSymbol(syntax) ? semanticModel.GetSymbolInfo(syntax).Symbol : null;
    var shouldDeferToOperation = ShouldDeferSyntaxTypeToOperation(syntax);
    var typeResolution = shouldDeferToOperation
      ? new SyntaxTypeResolution(null, QueriedSemanticModel: false, ReusedReferencedSymbolType: false)
      : ResolveSyntaxTypeSymbol(syntax, semanticModel, referencedSymbol);
    var queriedDeclaredSymbol = CanDeclareSymbol(syntax);
    return new SyntaxSemanticFacts(
      queriedDeclaredSymbol ? semanticModel.GetDeclaredSymbol(syntax) : null,
      queriedDeclaredSymbol,
      referencedSymbol,
      typeResolution,
      shouldDeferToOperation);
  }

  private bool ShouldUsePartitionedSyntaxPass(
    RoslynCpgBuildContext context,
    IReadOnlyList<OperationRootPlan> operationRoots)
  {
    return true;
  }
}
