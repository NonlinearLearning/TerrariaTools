using Microsoft.CodeAnalysis;

namespace NLCPG.Builder;

public sealed partial class NLCPGBuilder
{
    private sealed record SyntaxSemanticFacts(
      ISymbol? DeclaredSymbol,
      bool QueriedDeclaredSymbol,
      ISymbol? ReferencedSymbol,
      SyntaxTypeResolution TypeResolution,
      bool ShouldDeferToOperation);

    private void RunSyntaxPass(NLCPGBuildContext context, bool usePartitionedSyntaxPass, IReadOnlyList<OperationRootPlan> operationRoots)
    {
        if (!usePartitionedSyntaxPass)
        {
            // 关闭分区模式时完全回退到 legacy 路径，保证行为边界清晰。
            RunLegacySyntaxPass(context);
            return;
        }

        // 分区根只保留最外层 body，避免嵌套方法体在不同分区里重复分析。
        var partitionRoots = operationRoots
          .Select(root => root.BodySyntax)
          .Where(root => !operationRoots.Any(other =>
            !ReferenceEquals(root, other.BodySyntax) && root.Ancestors().Contains(other.BodySyntax)))
          .ToArray();
        // 每个分区直接展开成完整语法节点数组，后续 worker 只读这些节点并采集事实。
        var partitions = partitionRoots
          .Select(root => root.DescendantNodesAndSelf().ToArray())
          .ToArray();
        var partitionSyntax = new HashSet<SyntaxNode>(
          partitions.SelectMany(nodes => nodes),
          ReferenceEqualityComparer.Instance);
        foreach (var syntax in context.Root.DescendantNodesAndSelf().Where(node => !partitionSyntax.Contains(node)))
        {
            // 分区外节点不会进入 worker，因此先在主线程补齐缓存。
            _partitionedSyntaxFacts[syntax] = AnalyzeSyntaxFacts(syntax, context.SemanticModel);
        }

        // 分区内语义事实异步采集完成后统一回填缓存，再进入真正的有序建图阶段。
        var results = RunSyntaxPartitionsAsync(partitions, context.SemanticModel).GetAwaiter().GetResult();
        foreach (var facts in results)
        {
            foreach (var entry in facts)
            {
                _partitionedSyntaxFacts[entry.Key] = entry.Value;
            }
        }
        RunPartitionedSyntaxPass(context, partitionRoots, partitions);
        _partitionedSyntaxFacts.Clear();
    }

    // 并发跑每个语法分区的语义采集；返回值只包含只读事实，不直接触碰图状态。
    private async Task<IReadOnlyList<IReadOnlyDictionary<SyntaxNode, SyntaxSemanticFacts>>> RunSyntaxPartitionsAsync(IReadOnlyList<SyntaxNode[]> partitions, SemanticModel semanticModel)
    {
        return await _concurrencyPool.SelectOrderedAsync(
          partitions,
          _options.EffectiveMaxDegreeOfParallelism,
          (partition, _, _) => Task.FromResult<IReadOnlyDictionary<SyntaxNode, SyntaxSemanticFacts>>(
            AnalyzeSyntaxPartition(partition, semanticModel)));
    }

    // 对单个语法分区逐节点采集声明、引用和类型事实，供后续提交阶段复用。
    private IReadOnlyDictionary<SyntaxNode, SyntaxSemanticFacts> AnalyzeSyntaxPartition(IReadOnlyList<SyntaxNode> syntaxNodes, SemanticModel semanticModel)
    {
        var facts = new Dictionary<SyntaxNode, SyntaxSemanticFacts>(ReferenceEqualityComparer.Instance);
        foreach (var syntax in syntaxNodes)
        {
            // 这里只产出缓存数据，不创建图节点，避免 worker 线程污染共享状态。
            facts[syntax] = AnalyzeSyntaxFacts(syntax, semanticModel);
        }

        return facts;
    }

    private SyntaxSemanticFacts AnalyzeSyntaxFacts(SyntaxNode syntax, SemanticModel semanticModel)
    {
        var referencedSymbol = CanReferenceSymbol(syntax) ? semanticModel.GetSymbolInfo(syntax).Symbol : null;
        var shouldDeferToOperation = ShouldDeferSyntaxTypeToOperation(syntax);
        // 需要交给 OperationPass 回填类型的节点先留空，避免提前做重复的 GetTypeInfo。
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

    private bool ShouldUsePartitionedSyntaxPass(NLCPGBuildContext context, IReadOnlyList<OperationRootPlan> operationRoots)
    {
        return true;
    }
}
