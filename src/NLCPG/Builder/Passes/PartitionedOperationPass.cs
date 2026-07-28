using System.Buffers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Streaming;
using NLCPG.Model;

namespace NLCPG.Builder;

public sealed partial class NLCPGBuilder
{
    private sealed record OperationRootPlan(
      SyntaxNode BodySyntax,
      IMethodSymbol? OwningMethod,
      int Order);

    private sealed record OperationFragmentRecord(
      IOperation Operation,
      IOperation? ParentOperation,
      IMethodSymbol? OwningMethod);

    private sealed record OperationPartitionResult(
      int Order,
      int DeclarationSpanStart,
      int DeclarationSpanEnd,
      int BodySpanStart,
      int BodySpanEnd,
      string? OwningMethodSymbolKey,
      IReadOnlyList<OperationFragmentRecord> Records);

    private sealed record OperationBuildStrategy(
      NLCPGBuilderMode ExecutedMode,
      bool UsePartitionedOperationBuild,
      int SourceLineCount,
      IReadOnlyList<OperationRootPlan> OperationRoots);

    private void RunPartitionedOperationPass(NLCPGBuildContext context, IReadOnlyList<OperationRootPlan> operationRoots, SkeletonShardPublisher? streamingPublisher = null)
    {
        if (operationRoots.Count == 0)
        {
            return;
        }

        // 并行部分只读取 SemanticModel 并收集记录；节点、边和分片由按源顺序的提交回调统一物化。
        _concurrencyPool.CommitOrdered(
          operationRoots,
          new NL.Concurrency.ConcurrencyWindowOptions(
            _options.EffectiveMaxDegreeOfParallelism,
            _options.EffectiveOrderedResultReorderAllowance,
            _options.EffectiveMaxOrderedResultRecordCount),
          (rootPlan, _) => AnalyzeOperationPartition(rootPlan, context.SemanticModel),
          (partition, order) =>
          {
              if (partition.Order != order)
              {
                  throw new InvalidOperationException("Operation fragment order does not match the ordered commit slot.");
              }

              OperationFragmentFacts? facts = null;
              try
              {
                  facts = MaterializeOperationPartition(partition, context.Graph);
                  foreach (var record in partition.Records)
                  {
                      var operationNode = GetOrCreateOperationNode(record.Operation, context.Graph);
                      context.AddOperationInventoryEntry(
                    record.Operation,
                    partition.Records[0].Operation,
                    record.OwningMethod,
                    record.ParentOperation is null,
                    operationNode);
                  }

                  if (streamingPublisher is not null)
                  {
                      streamingPublisher.PublishOperationFragmentAsync(context, facts, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                  }
              }
              finally
              {
                  facts?.Release();
              }
          },
          retainedRecordCount: partition => partition.Records.Count);
    }

    private OperationPartitionResult AnalyzeOperationPartition(OperationRootPlan rootPlan, SemanticModel semanticModel)
    {
        var rootOperation = semanticModel.GetOperation(rootPlan.BodySyntax);
        var records = new List<OperationFragmentRecord>();
        if (rootOperation is null)
        {
            return CreatePartitionResult(rootPlan, records);
        }

        // 用显式栈保留 Roslyn 的子节点顺序，同时避免深层操作树递归遍历造成栈溢出。
        var pending = new Stack<(IOperation Operation, IOperation? Parent)>();
        var childBuffer = ArrayPool<IOperation>.Shared.Rent(minimumLength: 8);
        try
        {
            pending.Push((rootOperation, Parent: null));
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                records.Add(new OperationFragmentRecord(current.Operation, current.Parent, rootPlan.OwningMethod));

                var childCount = 0;
                foreach (var child in current.Operation.ChildOperations)
                {
                    if (childCount == childBuffer.Length)
                    {
                        childBuffer = GrowChildBuffer(childBuffer, childCount + 1);
                    }

                    childBuffer[childCount] = child;
                    childCount += 1;
                }

                for (var index = childCount - 1; index >= 0; index -= 1)
                {
                    pending.Push((childBuffer[index], current.Operation));
                }
            }
        }
        finally
        {
            ArrayPool<IOperation>.Shared.Return(childBuffer, clearArray: true);
        }

        return CreatePartitionResult(rootPlan, records);
    }

    private static IOperation[] GrowChildBuffer(IOperation[] buffer, int requiredLength)
    {
        var expanded = ArrayPool<IOperation>.Shared.Rent(Math.Max(requiredLength, buffer.Length * 2));
        Array.Copy(buffer, expanded, buffer.Length);
        ArrayPool<IOperation>.Shared.Return(buffer, clearArray: true);
        return expanded;
    }

    private static OperationPartitionResult CreatePartitionResult(OperationRootPlan rootPlan, IReadOnlyList<OperationFragmentRecord> records)
    {
        var declarationSpan = rootPlan.BodySyntax.Parent?.Span ?? rootPlan.BodySyntax.Span;
        var owningMethodSymbolKey = rootPlan.OwningMethod is null
          ? null
          : rootPlan.OwningMethod.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new OperationPartitionResult(
          rootPlan.Order,
          declarationSpan.Start,
          declarationSpan.End,
          rootPlan.BodySyntax.SpanStart,
          rootPlan.BodySyntax.Span.End,
          owningMethodSymbolKey,
          records);
    }

    private OperationFragmentFacts MaterializeOperationPartition(OperationPartitionResult partition, NLCPGGraph graph)
    {
        var nodeDescriptors = new List<CpgNodeDescriptor>(partition.Records.Count);
        var describedAnchors = new HashSet<StableNodeAnchor>();
        var edgeCandidates = new List<CpgEdgeCandidate>(partition.Records.Count * 4);
        // 当前方法运行在有序提交阶段，因此可安全写入图，并为流式持久化收集稳定锚点描述。
        foreach (var record in partition.Records)
        {
            var operationNode = GetOrCreateOperationNode(record.Operation, graph);
            if (describedAnchors.Add(operationNode.StableAnchor!.Value))
            {
                nodeDescriptors.Add(CpgNodeDescriptor.FromNode(operationNode));
            }
            if (record.ParentOperation is not null)
            {
                var parentNode = GetOrCreateOperationNode(record.ParentOperation, graph);
                var edgeKind = SelectOperationEdge(record.ParentOperation, record.Operation);
                graph.AddEdge(parentNode, operationNode, edgeKind);
                edgeCandidates.Add(CreateEdgeCandidate(parentNode, operationNode, edgeKind));
            }

            if (_syntaxNodes.TryGetValue(record.Operation.Syntax, out var syntaxNode))
            {
                graph.AddEdge(syntaxNode, operationNode, Contracts.NLCPGEdgeKind.SyntaxHasOperation);
                graph.AddEdge(operationNode, syntaxNode, Contracts.NLCPGEdgeKind.OpHasSyntax);
                edgeCandidates.Add(CreateEdgeCandidate(syntaxNode, operationNode, Contracts.NLCPGEdgeKind.SyntaxHasOperation));
                edgeCandidates.Add(CreateEdgeCandidate(operationNode, syntaxNode, Contracts.NLCPGEdgeKind.OpHasSyntax));
            }

            AddTypeEdges(operationNode, record.Operation.Type, graph);
            AddEvalTypeEdge(operationNode, record.Operation.Type, graph);
            AddOperationBackedSyntaxTypeEdge(record.Operation, graph);

            var resolvedSymbol = ResolveOperationSymbol(record.Operation);
            if (resolvedSymbol is not null)
            {
                var symbolNode = GetOrCreateSymbolNode(resolvedSymbol, graph);
                graph.AddEdge(operationNode, symbolNode, Contracts.NLCPGEdgeKind.OpResolvesToSymbol);
            }
        }

        return new OperationFragmentFacts(
          partition.Order,
          partition.DeclarationSpanStart,
          partition.DeclarationSpanEnd,
          partition.BodySpanStart,
          partition.BodySpanEnd,
          partition.OwningMethodSymbolKey,
          nodeDescriptors.ToArray(),
          edgeCandidates.ToArray());
    }

    private static CpgEdgeCandidate CreateEdgeCandidate(NLCPGNode source, NLCPGNode target, Contracts.NLCPGEdgeKind kind)
    {
        return new CpgEdgeCandidate(
          source.StableAnchor ?? throw new InvalidOperationException("Operation fragment edges require stable source anchors."),
          target.StableAnchor ?? throw new InvalidOperationException("Operation fragment edges require stable target anchors."),
          kind,
          StructuredLabel: null,
          ContextId: null,
          CallSiteContext: null);
    }

    private OperationBuildStrategy CreateOperationBuildStrategy(NLCPGBuildContext context)
    {
        var operationRoots = GetOperationRootPlans(context.Root, context.SemanticModel);
        var sourceLineCount = CountSourceLines(context.Source);
        return new OperationBuildStrategy(
          NLCPGBuilderMode.Partitioned,
          UsePartitionedOperationBuild: true,
          sourceLineCount,
          operationRoots);
    }

    private bool ShouldUsePartitionedOperationBuild(IReadOnlyList<OperationRootPlan> operationRoots, int sourceLineCount)
    {
        return true;
    }

    private static int CountSourceLines(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return 0;
        }

        var lineCount = 1;
        foreach (var character in source)
        {
            if (character == '\n')
            {
                lineCount += 1;
            }
        }

        return lineCount;
    }

    private static int CountLineSpan(SyntaxNode syntax)
    {
        var span = syntax.SyntaxTree.GetLineSpan(syntax.Span);
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }
}
