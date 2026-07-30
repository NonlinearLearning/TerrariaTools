using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Analysis;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgRelationQueryUnitTests
{
    [Fact]
    public void Query_ProfileAndDirection_ReturnsStablePathWithoutDataFlowFallback()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var parent = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "parent"));
        var child = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "child"));
        var unrelated = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "unrelated"));
        graph.AddEdge(parent, child, NLCPGEdgeKind.SyntaxChild);
        graph.AddEdge(child, unrelated, NLCPGEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph);
        var query = CreateQuery(graph, "parent", "child", CpgRelationProfile.StructuralContainment);

        // Act
        var first = service.Query(query);
        var second = service.Query(query);

        // Assert
        Assert.Equal(CpgQueryStatus.Complete, first.Status);
        Assert.Single(first.Paths);
        Assert.All(first.Edges, edge => Assert.Equal(NLCPGEdgeKind.SyntaxChild, edge.Kind));
        Assert.True(second.WasCacheHit);
    }

    [Fact]
    public void Query_MissingCapability_ReturnsUnavailableInsteadOfEmptyCompleteResult()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNode(NLCPGNodeKind.Operation, "Operation", "source"));
        var sink = graph.AddNode(new NLCPGNode(NLCPGNodeKind.Operation, "Operation", "sink"));
        graph.AddEdge(source, sink, NLCPGEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph, NLCPGCapability.SyntaxSemantic);

        // Act
        var result = service.Query(CreateQuery(graph, "source", "sink", CpgRelationProfile.LocalDataFlow));

        // Assert
        Assert.Equal(CpgQueryStatus.Unavailable, result.Status);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void Query_VisitedNodeBudget_ReturnsTruncated()
    {
        // Arrange
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "source"));
        var middle = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "middle"));
        var sink = graph.AddNode(new NLCPGNode(NLCPGNodeKind.SyntaxNode, "SyntaxNode", "sink"));
        graph.AddEdge(source, middle, NLCPGEdgeKind.SyntaxChild);
        graph.AddEdge(middle, sink, NLCPGEdgeKind.SyntaxChild);
        graph.FreezeQueryIndex();
        var service = new CpgRelationQueryService(graph);
        var query = CreateQuery(graph, "source", "sink", CpgRelationProfile.StructuralContainment) with
        {
            Budget = new NLCPGTraversalBudget(4, 1, 1, 2, 16),
        };

        // Act
        var result = service.Query(query);

        // Assert
        Assert.Equal(CpgQueryStatus.Truncated, result.Status);
        Assert.Equal("maxVisitedNodes", result.TruncationReason);
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("edges")]
    [InlineData("cache")]
    [InlineData("fanout")]
    public void MarkAnalysisSnapshot_DifferentCompleteBudget_DoesNotReuseSliceResult(string changedField)
    {
        // Arrange
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNode(NLCPGNodeKind.Operation, "Operation", "source"));
        var sink = graph.AddNode(new NLCPGNode(NLCPGNodeKind.Operation, "Operation", "sink"));
        graph.AddEdge(source, sink, NLCPGEdgeKind.DataFlow);
        graph.FreezeQueryIndex();
        var tree = CSharpSyntaxTree.ParseText("class Sample { }");
        var compilation = CSharpCompilation.Create(
            "slice-cache-budget",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var snapshot = new MarkAnalysisSnapshot(new CpgAnalysisContext(
            graph,
            compilation.GetSemanticModel(tree),
            tree.GetRoot()));
        var sinkNodeId = graph.Nodes.Single(node => node.Name == "sink").NodeId!.Value;
        var baseline = new NLCPGSliceQueryOptions(
            new HashSet<NLCPGEdgeKind> { NLCPGEdgeKind.DataFlow },
            2,
            4,
            4,
            MaxVisitedNodes: 16,
            MaxVisitedEdges: 16,
            MaxCachedStates: 16,
            MaxCallerFanout: 16);
        var changed = changedField switch
        {
            "nodes" => baseline with { MaxVisitedNodes = 15 },
            "edges" => baseline with { MaxVisitedEdges = 15 },
            "cache" => baseline with { MaxCachedStates = 15 },
            "fanout" => baseline with { MaxCallerFanout = 15 },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField)),
        };

        // Act
        var first = snapshot.QuerySliceBackward(sinkNodeId, baseline);
        var second = snapshot.QuerySliceBackward(sinkNodeId, changed);

        // Assert
        Assert.NotSame(first, second);
    }

    private static CpgRelationQuery CreateQuery(
        NLCPGGraph graph,
        string sourceName,
        string targetName,
        CpgRelationProfile profile)
    {
        var source = graph.Nodes.Single(node => node.Name == sourceName).NodeId!.Value;
        var target = graph.Nodes.Single(node => node.Name == targetName).NodeId!.Value;
        return new CpgRelationQuery(
            profile,
            CpgQueryDirection.Outgoing,
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { source }),
            new CpgNodeSelector(NodeIds: new HashSet<NodeId> { target }),
            new NLCPGTraversalBudget(4, 1, 1, 16, 16),
            CpgRelationProfiles.GetRequiredCapabilities(profile));
    }
}
