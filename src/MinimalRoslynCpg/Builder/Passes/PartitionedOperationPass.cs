using System.Buffers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder;

public sealed partial class RoslynCpgBuilder
{
  private sealed record OperationRootPlan(
    SyntaxNode BodySyntax,
    IMethodSymbol? OwningMethod,
    int Order);

  private sealed record OperationPartitionRecord(
    IOperation Operation,
    IOperation? ParentOperation,
    IMethodSymbol? OwningMethod);

  private sealed record OperationPartitionResult(
    int Order,
    IReadOnlyList<OperationPartitionRecord> Records);

  private sealed record OperationBuildStrategy(
    RoslynCpgBuilderMode ExecutedMode,
    bool UsePartitionedOperationBuild,
    int SourceLineCount,
    IReadOnlyList<OperationRootPlan> OperationRoots);

  private void RunPartitionedOperationPass(RoslynCpgBuildContext context, IReadOnlyList<OperationRootPlan> operationRoots)
  {
    if (operationRoots.Count == 0)
    {
      return;
    }

    var partitionTasks = RunOperationPartitionsAsync(operationRoots, context.SemanticModel).GetAwaiter().GetResult();

    foreach (var partition in partitionTasks.OrderBy(result => result.Order))
    {
      MaterializeOperationPartition(partition, context.Graph);
    }
  }

  private async Task<IReadOnlyList<OperationPartitionResult>> RunOperationPartitionsAsync(
    IReadOnlyList<OperationRootPlan> operationRoots,
    SemanticModel semanticModel)
  {
    return await BoundedPartitionWorkWindow.RunAsync(
      operationRoots,
      _options.EffectiveMaxDegreeOfParallelism,
      (rootPlan, _) => AnalyzeOperationPartition(rootPlan, semanticModel));
  }

  private OperationPartitionResult AnalyzeOperationPartition(OperationRootPlan rootPlan, SemanticModel semanticModel)
  {
    var rootOperation = semanticModel.GetOperation(rootPlan.BodySyntax);
    var records = new List<OperationPartitionRecord>();
    if (rootOperation is null)
    {
      return new OperationPartitionResult(rootPlan.Order, records);
    }

    var pending = new Stack<(IOperation Operation, IOperation? Parent)>();
    var childBuffer = ArrayPool<IOperation>.Shared.Rent(minimumLength: 8);
    Interlocked.Increment(ref _operationChildBufferRentCount);
    try
    {
      pending.Push((rootOperation, Parent: null));
      while (pending.Count > 0)
      {
        var current = pending.Pop();
        records.Add(new OperationPartitionRecord(current.Operation, current.Parent, rootPlan.OwningMethod));

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

    return new OperationPartitionResult(rootPlan.Order, records);
  }

  private static IOperation[] GrowChildBuffer(IOperation[] buffer, int requiredLength)
  {
    var expanded = ArrayPool<IOperation>.Shared.Rent(Math.Max(requiredLength, buffer.Length * 2));
    Array.Copy(buffer, expanded, buffer.Length);
    ArrayPool<IOperation>.Shared.Return(buffer, clearArray: true);
    return expanded;
  }

  private void MaterializeOperationPartition(OperationPartitionResult partition, RoslynCpgGraph graph)
  {
    foreach (var record in partition.Records)
    {
      var operationNode = GetOrCreateOperationNode(record.Operation, graph);
      if (record.OwningMethod is not null && !_operationOwningMethods.ContainsKey(record.Operation))
      {
        _operationOwningMethods[record.Operation] = record.OwningMethod;
      }

      if (record.ParentOperation is not null)
      {
        var parentNode = GetOrCreateOperationNode(record.ParentOperation, graph);
        graph.AddEdge(parentNode, operationNode, SelectOperationEdge(record.ParentOperation, record.Operation));
      }

      if (_syntaxNodes.TryGetValue(record.Operation.Syntax, out var syntaxNode))
      {
        graph.AddEdge(syntaxNode, operationNode, Contracts.RoslynCpgEdgeKind.SyntaxHasOperation);
        graph.AddEdge(operationNode, syntaxNode, Contracts.RoslynCpgEdgeKind.OpHasSyntax);
      }

      AddTypeEdges(operationNode, record.Operation.Type, graph);
      AddEvalTypeEdge(operationNode, record.Operation.Type, graph);
      AddOperationBackedSyntaxTypeEdge(record.Operation, graph);

      var resolvedSymbol = ResolveOperationSymbol(record.Operation);
      if (resolvedSymbol is not null)
      {
        var symbolNode = GetOrCreateSymbolNode(resolvedSymbol, graph);
        graph.AddEdge(operationNode, symbolNode, Contracts.RoslynCpgEdgeKind.OpResolvesToSymbol);
      }
    }
  }

  private OperationBuildStrategy CreateOperationBuildStrategy(RoslynCpgBuildContext context)
  {
    var operationRoots = GetOperationRootPlans(context.Root, context.SemanticModel);
    var sourceLineCount = CountSourceLines(context.Source);
    return new OperationBuildStrategy(
      RoslynCpgBuilderMode.Partitioned,
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
