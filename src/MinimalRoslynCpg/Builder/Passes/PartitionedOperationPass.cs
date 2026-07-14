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
    using var gate = new SemaphoreSlim(_options.EffectiveMaxDegreeOfParallelism);
    var tasks = operationRoots
      .Select(async rootPlan =>
      {
        await gate.WaitAsync();
        try
        {
          return await Task.Run(() => AnalyzeOperationPartition(rootPlan, semanticModel));
        }
        finally
        {
          gate.Release();
        }
      })
      .ToArray();
    return await Task.WhenAll(tasks);
  }

  private static OperationPartitionResult AnalyzeOperationPartition(OperationRootPlan rootPlan, SemanticModel semanticModel)
  {
    var rootOperation = semanticModel.GetOperation(rootPlan.BodySyntax);
    var records = new List<OperationPartitionRecord>();
    if (rootOperation is null)
    {
      return new OperationPartitionResult(rootPlan.Order, records);
    }

    var pending = new Stack<(IOperation Operation, IOperation? Parent)>();
    pending.Push((rootOperation, Parent: null));
    while (pending.Count > 0)
    {
      var current = pending.Pop();
      records.Add(new OperationPartitionRecord(current.Operation, current.Parent, rootPlan.OwningMethod));

      var children = current.Operation.ChildOperations.ToArray();
      for (var index = children.Length - 1; index >= 0; index -= 1)
      {
        pending.Push((children[index], current.Operation));
      }
    }

    return new OperationPartitionResult(rootPlan.Order, records);
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
    if (_options.BuildMode == RoslynCpgBuilderMode.Legacy)
    {
      return new OperationBuildStrategy(
        RoslynCpgBuilderMode.Legacy,
        UsePartitionedOperationBuild: false,
        sourceLineCount,
        operationRoots);
    }

    var shouldUsePartitioned = _options.BuildMode == RoslynCpgBuilderMode.Partitioned ||
      ShouldUsePartitionedOperationBuild(operationRoots, sourceLineCount);
    return new OperationBuildStrategy(
      shouldUsePartitioned ? RoslynCpgBuilderMode.Partitioned : RoslynCpgBuilderMode.Legacy,
      shouldUsePartitioned,
      sourceLineCount,
      operationRoots);
  }

  private bool ShouldUsePartitionedOperationBuild(IReadOnlyList<OperationRootPlan> operationRoots, int sourceLineCount)
  {
    if (_options.BuildMode == RoslynCpgBuilderMode.Partitioned)
    {
      return operationRoots.Count > 1;
    }

    if (_options.BuildMode != RoslynCpgBuilderMode.Auto || operationRoots.Count <= 1)
    {
      return false;
    }

    var maxMethodLineSpan = operationRoots.Count == 0
      ? 0
      : operationRoots.Max(rootPlan => CountLineSpan(rootPlan.BodySyntax));
    return sourceLineCount >= _options.LargeFileLineThreshold &&
      operationRoots.Count >= _options.LargeFileMethodThreshold &&
      maxMethodLineSpan >= _options.LargeMethodLineSpanThreshold;
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
