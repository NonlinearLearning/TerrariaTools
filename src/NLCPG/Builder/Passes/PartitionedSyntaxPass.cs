using Microsoft.CodeAnalysis;

namespace NLCPG.Builder;

public sealed partial class NLCPGBuilder
{
    private sealed record SyntaxPartitionResult(
      int Order,
      int SpanStart,
      int SpanEnd,
      IReadOnlyDictionary<SyntaxNode, SyntaxSemanticFacts> Facts,
      long CollectionElapsedMilliseconds);

    private sealed record SyntaxSemanticFacts(
      ISymbol? DeclaredSymbol,
      bool QueriedDeclaredSymbol,
      ISymbol? ReferencedSymbol,
      SyntaxTypeResolution TypeResolution,
      bool ShouldDeferToOperation);

    private void RunSyntaxPass(
      NLCPGBuildContext context,
      bool usePartitionedSyntaxPass,
      IReadOnlyList<OperationRootPlan> operationRoots,
      CapabilityBuildPlan buildPlan)
    {
        if (!usePartitionedSyntaxPass)
        {
            // 关闭分区模式时完全回退到 legacy 路径，保证行为边界清晰。
            RunLegacySyntaxPass(context, buildPlan);
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
            _partitionedSyntaxFacts[syntax] = AnalyzeSyntaxFacts(syntax, context.SemanticModel, buildPlan);
        }

        // 分区内语义事实异步采集完成后统一回填缓存，再进入真正的有序建图阶段。
        var results = RunSyntaxPartitionsAsync(
          partitions,
          context.SemanticModel,
          buildPlan).GetAwaiter().GetResult();
        foreach (var result in results.OrderBy(result => result.Order))
        {
            foreach (var entry in result.Facts)
            {
                _partitionedSyntaxFacts[entry.Key] = entry.Value;
            }

            RecordPartitionPerformanceEvent(
              PartitionPerformanceStageId.SyntaxCollection,
              CreatePartitionPerformanceId(
                "syntax",
                result.Order,
                result.SpanStart,
                result.SpanEnd),
              result.Order,
              result.Facts.Count,
              result.Facts.Count,
              result.CollectionElapsedMilliseconds,
              result.CollectionElapsedMilliseconds);
        }
        RunPartitionedSyntaxPass(context, partitionRoots, partitions, buildPlan);
        _partitionedSyntaxFacts.Clear();
    }

    // 并发跑每个语法分区的语义采集；返回值只包含只读事实，不直接触碰图状态。
    private Task<IReadOnlyList<SyntaxPartitionResult>> RunSyntaxPartitionsAsync(
      IReadOnlyList<SyntaxNode[]> partitions,
      SemanticModel semanticModel,
      CapabilityBuildPlan buildPlan)
    {
        return _concurrencyPool.SelectCpuBoundOrdered(
          partitions,
          _options.EffectiveMaxDegreeOfParallelism,
          (partition, index, _) => AnalyzeSyntaxPartition(partition, index, semanticModel, buildPlan));
    }

    // 对单个语法分区逐节点采集声明、引用和类型事实，供后续提交阶段复用。
    private SyntaxPartitionResult AnalyzeSyntaxPartition(
      IReadOnlyList<SyntaxNode> syntaxNodes,
      int order,
      SemanticModel semanticModel,
      CapabilityBuildPlan buildPlan)
    {
        var stopwatch = PartitionPerformanceDiagnosticsEnabled
          ? System.Diagnostics.Stopwatch.StartNew()
          : null;
        var facts = new Dictionary<SyntaxNode, SyntaxSemanticFacts>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (var syntax in syntaxNodes)
            {
                // 这里只产出缓存数据，不创建图节点，避免 worker 线程污染共享状态。
                facts[syntax] = AnalyzeSyntaxFacts(syntax, semanticModel, buildPlan);
            }

            return new SyntaxPartitionResult(
              order,
              syntaxNodes.Count == 0 ? 0 : syntaxNodes[0].SpanStart,
              syntaxNodes.Count == 0 ? 0 : syntaxNodes[^1].Span.End,
              facts,
              stopwatch?.ElapsedMilliseconds ?? 0);
        }
        finally
        {
            stopwatch?.Stop();
        }
    }

    private SyntaxSemanticFacts AnalyzeSyntaxFacts(
      SyntaxNode syntax,
      SemanticModel semanticModel,
      CapabilityBuildPlan buildPlan)
    {
        var referencedSymbol = buildPlan.EmitReferences && CanReferenceSymbol(syntax)
          ? semanticModel.GetSymbolInfo(syntax).Symbol
          : null;
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
