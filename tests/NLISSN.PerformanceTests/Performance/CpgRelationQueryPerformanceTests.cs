using NLCPG.Analysis;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

public sealed class CpgRelationQueryPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public CpgRelationQueryPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Query_WarmedCache_ReportsExecutionAndPathMaterializationSeparately()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "source"));
        var target = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "target"));
        graph.AddEdge(source, target, NLCPGEdgeKind.SyntaxChild);
        graph.FreezeQueryIndex();
        var sourceId = graph.Nodes.Single(node => node.Name == "source").NodeId!.Value;
        var targetId = graph.Nodes.Single(node => node.Name == "target").NodeId!.Value;
        var query = new CpgRelationQuery(
            CpgRelationProfile.StructuralContainment,
            CpgQueryDirection.Outgoing,
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { sourceId }),
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { targetId }),
            new NLCPGTraversalBudget(4, 1, 1, 16, 16),
            NLCPGCapability.SyntaxSemantic);
        var service = new CpgRelationQueryService(graph);

        var cold = service.Query(query);
        var warm = service.Query(query);

        Assert.NotNull(cold.Metrics);
        Assert.NotNull(warm.Metrics);
        Assert.False(cold.WasCacheHit);
        Assert.True(warm.WasCacheHit);
        Assert.Equal(1, warm.Metrics!.CacheHitCount);
        _output.WriteLine(
            $"query-execution-ms={cold.Metrics!.QueryExecutionElapsedMilliseconds}; " +
            $"path-materialization-ms={cold.Metrics.PathMaterializationElapsedMilliseconds}; " +
            $"shard-load-ms={cold.Metrics.ShardLoadElapsedMilliseconds}; " +
            $"cache-hits={warm.Metrics.CacheHitCount}");
    }
}
