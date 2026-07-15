using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Analysis;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RoslynCpgSliceQueryTests
{
    [Fact]
    public void QueryBackward_ReturnsStablePathsAndReportsHopBudgetExhaustion()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var middle = CreateNode("middle");
        var sink = CreateNode("sink");
        graph.AddEdge(source, middle, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(middle, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 1,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var result = query.QueryBackward("sink", options);

        var path = Assert.Single(result.Paths);
        Assert.Equal("middle", path.SourceNodeId);
        Assert.Equal("sink", path.SinkNodeId);
        Assert.Equal(new[] { "middle", "sink" }, path.NodeIds);
        Assert.True(result.WasTruncated);
        Assert.Equal("maxHops", result.TruncationReason);
    }

    [Fact]
    public void MarkAnalysisSnapshot_ReusesSliceResultForSameQueryKey()
    {
        const string source = "public sealed class Sample { public int Run(int value) { return value; } }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "slice-cache.cs");
        var compilation = CSharpCompilation.Create(
            "SliceCacheTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "slice-cache.cs");
        var analysisContext = new CpgAnalysisContext(
            graph,
            compilation.GetSemanticModel(tree),
            tree.GetRoot());
        var snapshot = new MarkAnalysisSnapshot(analysisContext);
        var ruleContext = new RuleContext(
            analysisContext,
            new Dictionary<string, string>(),
            markAnalysisSnapshot: snapshot);
        var sinkNode = graph.Nodes.First(node => node.Kind == RoslynCpgNodeKind.MethodReturn);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 3,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var first = ruleContext.QuerySliceBackward(sinkNode.Id, options);
        var second = ruleContext.QuerySliceBackward(sinkNode.Id, options);

        Assert.Same(first, second);
        Assert.Equal(1, snapshot.Telemetry.SliceQueryCacheMissCount);
        Assert.Equal(1, snapshot.Telemetry.SliceQueryCacheHitCount);
        Assert.Equal(
            graph.Edges
                .Where(edge => edge.SourceId == sinkNode.Id && edge.Kind == RoslynCpgEdgeKind.DataFlow)
                .OrderBy(edge => edge.TargetId, StringComparer.Ordinal),
            ruleContext.GetGraphEdgesByKind(sinkNode.Id, RoslynCpgEdgeKind.DataFlow)
                .OrderBy(edge => edge.TargetId, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(1, 10, "maxPaths")]
    [InlineData(10, 1, "maxDefinitions")]
    public void QueryBackward_WhenResultBudgetIsReached_ReportsTheExhaustedBudget(
        int maxPaths,
        int maxDefinitions,
        string expectedReason)
    {
        var graph = new RoslynCpgGraph();
        var firstSource = CreateNode("first-source");
        var secondSource = CreateNode("second-source");
        var sink = CreateNode("sink");
        graph.AddEdge(firstSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(secondSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 2,
            MaxPaths: maxPaths,
            MaxDefinitions: maxDefinitions);

        var result = query.QueryBackward(sink.Id, options);

        Assert.True(result.WasTruncated);
        Assert.Equal(expectedReason, result.TruncationReason);
        Assert.True(result.Paths.Count <= maxPaths);
        Assert.True(result.Paths.Count <= maxDefinitions);
        Assert.Equal(
            result.Paths.OrderBy(path => path.SourceNodeId, StringComparer.Ordinal),
            result.Paths);
    }

    [Fact]
    public void QueryBackward_WhenTraversalContainsCycle_TerminatesWithStableEmptyResult()
    {
        var graph = new RoslynCpgGraph();
        var first = CreateNode("first");
        var second = CreateNode("second");
        var sink = CreateNode("sink");
        graph.AddEdge(first, second, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(second, first, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(second, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 10,
            MaxPaths: 10,
            MaxDefinitions: 10);

        var result = query.QueryBackward(sink.Id, options);

        Assert.Empty(result.Paths);
        Assert.False(result.WasTruncated);
        Assert.InRange(result.VisitedNodeCount, 1, 3);
    }

    [Fact]
    public void QueryBackward_WhenVisitedNodeBudgetIsReached_ReportsMaxVisitedNodes()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var middle = CreateNode("middle");
        var sink = CreateNode("sink");
        graph.AddEdge(source, middle, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(middle, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 4,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxVisitedNodes: 1);

        var result = query.QueryBackward(sink.Id, options);

        Assert.True(result.WasTruncated);
        Assert.Equal("maxVisitedNodes", result.TruncationReason);
        Assert.Equal(1, result.VisitedNodeCount);
    }

    [Fact]
    public void QueryBackward_RepeatedEquivalentQuery_UsesCache()
    {
        var graph = new RoslynCpgGraph();
        var firstSource = CreateNode("first-source");
        var secondSource = CreateNode("second-source");
        var sink = CreateNode("sink");
        graph.AddEdge(firstSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(secondSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 2,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxVisitedEdges: 2);

        var first = query.QueryBackward(sink.Id, options);
        var second = query.QueryBackward(sink.Id, options);

        Assert.False(first.WasTruncated);
        Assert.Equal(1, first.Telemetry.CacheMissCount);
        Assert.Equal(1, second.Telemetry.CacheHitCount);
        Assert.Equal(first.Paths, second.Paths);
    }

    [Fact]
    public void QueryBackward_WhenVisitedEdgeBudgetIsReached_ReportsMaxVisitedEdges()
    {
        var graph = new RoslynCpgGraph();
        var firstSource = CreateNode("first-source");
        var secondSource = CreateNode("second-source");
        var sink = CreateNode("sink");
        graph.AddEdge(firstSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.AddEdge(secondSource, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.DataFlow },
            MaxHops: 2,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxVisitedEdges: 1);

        var result = query.QueryBackward(sink.Id, options);

        Assert.True(result.WasTruncated);
        Assert.Equal("maxVisitedEdges", result.TruncationReason);
        Assert.Equal(1, result.VisitedEdgeCount);
    }

    [Fact]
    public void QueryBackward_InterproceduralEdgesConsumeOnlyCallDepthBudget()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var parameter = CreateNode("parameter");
        var sink = CreateNode("sink");
        graph.AddEdge(source, parameter, RoslynCpgEdgeKind.InterproceduralDataFlow);
        graph.AddEdge(parameter, sink, RoslynCpgEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var edgeKinds = new HashSet<RoslynCpgEdgeKind>
        {
            RoslynCpgEdgeKind.DataFlow,
            RoslynCpgEdgeKind.InterproceduralDataFlow,
        };

        var blocked = query.QueryBackward(sink.Id, new RoslynCpgSliceQueryOptions(edgeKinds, 4, 4, 4));
        var allowed = query.QueryBackward(sink.Id, new RoslynCpgSliceQueryOptions(edgeKinds, 4, 4, 4, MaxCallDepth: 1));

        Assert.True(blocked.WasTruncated);
        Assert.Equal("maxCallDepth", blocked.TruncationReason);
        Assert.Equal(new[] { "source", "parameter", "sink" }, Assert.Single(allowed.Paths).NodeIds);
    }

    [Fact]
    public void QueryBackward_WhenInterproceduralFrameRepeats_CutsTheRecursiveCallStack()
    {
        var graph = new RoslynCpgGraph();
        var source = CreateNode("source");
        var recursiveParameter = CreateNode("recursive-parameter");
        var sink = CreateNode("sink");
        graph.AddEdge(source, recursiveParameter, RoslynCpgEdgeKind.InterproceduralDataFlow, "ArgumentToParameter|callsite:recursive");
        graph.AddEdge(recursiveParameter, sink, RoslynCpgEdgeKind.InterproceduralDataFlow, "MethodReturnToCallResult|callsite:recursive");
        graph.FreezeQueryIndex();
        var query = new RoslynCpgSliceQuery(graph);
        var options = new RoslynCpgSliceQueryOptions(
            new HashSet<RoslynCpgEdgeKind> { RoslynCpgEdgeKind.InterproceduralDataFlow },
            MaxHops: 4,
            MaxPaths: 4,
            MaxDefinitions: 4,
            MaxCallDepth: 4);

        var result = query.QueryBackward(sink.Id, options);

        Assert.True(result.WasTruncated);
        Assert.Equal("callStackCycle", result.TruncationReason);
        Assert.True(result.Telemetry!.MaxObservedCallDepth >= 1);
    }

    private static RoslynCpgNode CreateNode(string id)
    {
        return new RoslynCpgNode(id, RoslynCpgNodeKind.Operation, "Operation");
    }
}
