using System.Buffers;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder.Streaming;
using NLCPG.Builder.Concurrency;
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
      IReadOnlyList<OperationFragmentRecord> Records,
      long CollectionElapsedMilliseconds = 0);

    private sealed record OperationWorkBatchResult(
      long BatchId,
      int StableOrder,
      IReadOnlyList<OperationPartitionResult> Partitions);

    private sealed record OperationPartitionPerformanceSample(
      OperationPartitionResult Partition,
      int MaterializedInputCount,
      int MaterializedOutputCount,
      long MaterializationElapsedMilliseconds);

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

        RunPartitionedOperationPassWithWorkBatches(context, operationRoots, streamingPublisher);
    }

    private void RunPartitionedOperationPassWithWorkBatches(
      NLCPGBuildContext context,
      IReadOnlyList<OperationRootPlan> operationRoots,
      SkeletonShardPublisher? streamingPublisher)
    {
        var materializationSamples = new List<OperationPartitionPerformanceSample>();
        var operationRootsByOrder = operationRoots.ToDictionary(root => root.Order);
        var workBatches = AssembleWorkBatches(context);
        _workBatchExecutor.ExecuteAsync(
          workBatches,
          (batch, _, _) =>
          {
              var partitions = batch.Items
                .OrderBy(item => item.StableOrder)
                .Select(item =>
                {
                    if (!operationRootsByOrder.TryGetValue(item.StableOrder, out var rootPlan))
                    {
                        throw new InvalidOperationException(
                          $"No operation root exists for WorkBatch item {item.StableOrder}.");
                    }

                    return AnalyzeOperationPartition(rootPlan, context.SemanticModel);
                })
                .ToArray();
              return new OperationWorkBatchResult(batch.BatchId, batch.StableOrder, partitions);
          },
          batchResult =>
          {
              foreach (var partition in batchResult.Partitions.OrderBy(partition => partition.Order))
              {
                  OperationFragmentFacts? facts = null;
                  var beforeNodeCount = context.Graph.Nodes.Count;
                  var materializationStopwatch = PartitionPerformanceDiagnosticsEnabled
                    ? Stopwatch.StartNew()
                    : null;
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
                      materializationStopwatch?.Stop();
                      if (PartitionPerformanceDiagnosticsEnabled)
                      {
                          materializationSamples.Add(
                            new OperationPartitionPerformanceSample(
                              partition,
                              partition.Records.Count,
                              Math.Max(0, context.Graph.Nodes.Count - beforeNodeCount),
                              materializationStopwatch?.ElapsedMilliseconds ?? 0));
                      }
                      facts?.Release();
                  }
              }
          },
          stageId: CpgWorkBatchPerformanceStageId.Operation).GetAwaiter().GetResult();

        foreach (var sample in materializationSamples.OrderBy(sample => sample.Partition.Order))
        {
            var partition = sample.Partition;
            var partitionId = CreatePartitionPerformanceId(
              "operation",
              partition.Order,
              partition.BodySpanStart,
              partition.BodySpanEnd);
            RecordPartitionPerformanceEvent(
              PartitionPerformanceStageId.OperationCollection,
              partitionId,
              partition.Order,
              1,
              partition.Records.Count,
              partition.CollectionElapsedMilliseconds,
              partition.CollectionElapsedMilliseconds);
            RecordPartitionPerformanceEvent(
              PartitionPerformanceStageId.OperationMaterialization,
              partitionId,
              partition.Order,
              sample.MaterializedInputCount,
              sample.MaterializedOutputCount,
              sample.MaterializationElapsedMilliseconds,
              sample.MaterializationElapsedMilliseconds);
        }
    }

    private OperationPartitionResult AnalyzeOperationPartition(OperationRootPlan rootPlan, SemanticModel semanticModel)
    {
        var collectionStopwatch = PartitionPerformanceDiagnosticsEnabled
          ? Stopwatch.StartNew()
          : null;
        var rootOperation = semanticModel.GetOperation(rootPlan.BodySyntax);
        var records = new List<OperationFragmentRecord>();
        try
        {
            if (rootOperation is not null)
            {
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
            }
        }
        finally
        {
            collectionStopwatch?.Stop();
        }

        return CreatePartitionResult(
          rootPlan,
          records,
          collectionStopwatch?.ElapsedMilliseconds ?? 0);
    }

    private static IOperation[] GrowChildBuffer(IOperation[] buffer, int requiredLength)
    {
        var expanded = ArrayPool<IOperation>.Shared.Rent(Math.Max(requiredLength, buffer.Length * 2));
        Array.Copy(buffer, expanded, buffer.Length);
        ArrayPool<IOperation>.Shared.Return(buffer, clearArray: true);
        return expanded;
    }

    private static OperationPartitionResult CreatePartitionResult(
      OperationRootPlan rootPlan,
      IReadOnlyList<OperationFragmentRecord> records,
      long collectionElapsedMilliseconds)
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
          records,
          collectionElapsedMilliseconds);
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

    /// <summary>
    /// 多文件聚合的批次装配（**D1 核心**）：把**所有文档**的方法根汇总成一批工作项，
    /// 交给同一个 <see cref="CpgWorkBatchBuilder"/> 装箱。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这才是 D1「跨文件凑标准大小」真正的落地点：单文件时若干小文件各自只有很少几个方法，
    /// 无法凑到标准大小；把它们的**完整方法**放进同一个装箱池，才能既保住
    /// 「原子单位是完整函数」又能接近标准大小。
    /// </para>
    /// <para>
    /// <b>为什么是正确且最小的做法：</b>每个工作项携带自己的
    /// <see cref="CpgWorkItem.SourceFilePath"/>（生产真相）与**文件内局部** <c>StableOrder</c>。
    /// 装箱器按 <see cref="CpgWorkItem.SourceFilePath"/> 排序后再装箱，
    /// 使同一文件的方法在池中连续 ⇒ 「按文件分组后组内保序」与单文件装箱**逐字同构**。
    /// 跨文件批次的 <c>ShardOrder</c> 由装箱器全局分配（T2 已完成）。
    /// </para>
    /// <para>
    /// 单文件时 <c>context.Documents</c> 恒为「自身」一项 ⇒ 产出与原
    /// <c>AssembleWorkBatches</c> **逐字相同**。
    /// </para>
    /// </remarks>
    internal IReadOnlyList<CpgWorkBatch> AssembleWorkBatchesAcrossDocuments(NLCPGBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.DocumentSet is null)
        {
            // 单文件：走原路径，保证行为与改造前逐字一致。
            return AssembleWorkBatches(context);
        }

        var workItems = new List<CpgWorkItem>();
        foreach (var document in context.Documents)
        {
            foreach (var rootPlan in GetOperationRootPlans(document.Root, document.SemanticModel))
            {
                var lineSpan = document.SemanticModel.SyntaxTree.GetLineSpan(rootPlan.BodySyntax.Span);
                var estimate = CpgWorkBatchCostModel.Estimate(
                  lineSpan.StartLinePosition.Line,
                  lineSpan.EndLinePosition.Line,
                  _options.EffectiveWorkBatchCostOptions);
                workItems.Add(new CpgWorkItem(
                  rootPlan.Order,
                  document.FilePath,
                  rootPlan.OwningMethod?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                  rootPlan.BodySyntax.SpanStart,
                  rootPlan.BodySyntax.Span.End,
                  estimate.Cost,
                  CpgWorkItemKind.Method));
            }
        }

        if (workItems.Count == 0)
        {
            return Array.Empty<CpgWorkBatch>();
        }

        // 多文件：label 仅用于展示，取首个文件；真正的路由键是每个工作项自己的 SourceFilePath。
        // S5-2：走 BuildWorkBatches（装箱的**唯一入口**），由其套用全局分片序号。
        return BuildWorkBatches(workItems[0].SourceFilePath, workItems);
    }

    /// <summary>
    /// 本构建的**任一**文档是否存在操作根。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 供各阶段的规划/执行守卫回答「本阶段到底有没有事可做」。
    /// </para>
    /// <para>
    /// ⚠ 必须是**跨全部文档**的判据。单文件时 <c>context.Documents</c> 恒为「自身」一项，
    /// 故等价于原来的 <c>GetOperationRootPlans(context.Root, …).Count != 0</c>，行为逐字不变；
    /// 而多文件时只查驱动文档会漏掉「首文件恰好无方法」的形态——
    /// 规划相位据此返回 null（不登记 plan），其余文件的边随之**静默全缺**。
    /// </para>
    /// </remarks>
    internal bool HasAnyOperationRootAcrossDocuments(NLCPGBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var document in context.Documents)
        {
            if (GetOperationRootPlans(document.Root, document.SemanticModel).Count != 0)
            {
                return true;
            }
        }

        return false;
    }

    internal IReadOnlyList<CpgWorkBatch> AssembleWorkBatches(NLCPGBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var workItems = GetOperationRootPlans(context.Root, context.SemanticModel)
          .Select(rootPlan =>
          {
              var lineSpan = context.SemanticModel.SyntaxTree.GetLineSpan(rootPlan.BodySyntax.Span);
              var estimate = CpgWorkBatchCostModel.Estimate(
                lineSpan.StartLinePosition.Line,
                lineSpan.EndLinePosition.Line,
                _options.EffectiveWorkBatchCostOptions);
              return new CpgWorkItem(
                rootPlan.Order,
                context.FilePath,
                rootPlan.OwningMethod?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                rootPlan.BodySyntax.SpanStart,
                rootPlan.BodySyntax.Span.End,
                estimate.Cost,
                CpgWorkItemKind.Method);
          })
          .ToArray();
        // S5-2：走 BuildWorkBatches（装箱的**唯一入口**），由其套用全局分片序号。
        return BuildWorkBatches(context.FilePath, workItems);
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
}
