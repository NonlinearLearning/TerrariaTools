using NLCPG.Builder;
using NLCPG.Builder.Concurrency;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgWorkBatchSyntaxTests
{
    [Fact]
    public async Task BatchFactCollection_ReturnsFactsWithoutMutatingTheSharedGraph()
    {
        var graph = new NLCPGGraph();
        var batches = CreateBatches(4);
        var reducedFacts = new List<SyntaxFact>();
        var executor = new CpgWorkBatchExecutor(new CpgWorkBatchExecutorOptions(
          maxDegreeOfParallelism: 2,
          queueCapacity: 2,
          fragmentSinkCapacity: 2));

        var facts = await executor.ExecuteAsync(
          batches,
          (batch, _, _) => new SyntaxFact(batch.StableOrder, batch.Items.Count),
          reducedFacts.Add);

        Assert.Empty(graph.Nodes);
        Assert.Equal(new[] { 0, 1, 2, 3 }, facts.Select(fact => fact.StableOrder));
        Assert.Equal(facts, reducedFacts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public void BatchSyntaxBuild_PreservesGraphSignatureAcrossDegreesOfParallelism(int dop)
    {
        const string source = "public sealed class Sample { public int First(int value) { return value + 1; } public int Second(int value) { return value + 2; } }";
        var baseline = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
        }).BuildFromSource(source, "batch-syntax.cs");
        var actual = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
        }).BuildFromSource(source, "batch-syntax.cs");

        Assert.Equal(Describe(baseline), Describe(actual));
    }

    private static IReadOnlyList<CpgWorkBatch> CreateBatches(int count)
    {
        return Enumerable.Range(0, count)
          .Select(index => new CpgWorkBatch(
            batchId: index,
            sourceFilePath: "batch-syntax.cs",
            stableOrder: index,
            items: new[]
            {
                new CpgWorkItem(
                  stableOrder: index,
                  sourceFilePath: "batch-syntax.cs",
                  methodSymbolKey: $"M{index}",
                  spanStart: index,
                  spanEnd: index,
                  estimatedCost: 1,
                  kind: CpgWorkItemKind.Method),
            },
            estimatedCost: 1,
            estimatedNodeCount: 1,
            estimatedBytes: 1,
            kind: CpgWorkBatchKind.Methods))
          .ToArray();
    }

    private static string[] Describe(NLCPGGraph graph)
    {
        var nodes = graph.Nodes.Select(node => string.Join(
          "|",
          node.NodeId,
          node.Kind,
          graph.ResolveDisplayKind(node),
          graph.ResolveName(node),
          graph.ResolveFullName(node),
          graph.ResolveFilePath(node),
          node.SpanStart,
          node.SpanEnd));
        var edges = graph.Edges.Select(edge => string.Join(
          "|",
          edge.SourceNodeId,
          edge.TargetNodeId,
          edge.Kind,
          edge.ContextId,
          edge.StructuredLabel?.StableKey));
        return nodes.Concat(edges).OrderBy(item => item, StringComparer.Ordinal).ToArray();
    }

    private sealed record SyntaxFact(int StableOrder, int ItemCount);
}
