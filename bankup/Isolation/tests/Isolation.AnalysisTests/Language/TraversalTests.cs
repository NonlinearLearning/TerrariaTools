using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Language;
using Xunit;

namespace Isolation.AnalysisTests.Language;

public sealed class TraversalTests
{
    [Fact]
    public void Methods_callsAndCallees_traverseSemanticCpgStyleDsl()
    {
        CpgGraph graph = new();
        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("Name", "Run");
        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", "Execute");
        CpgNode calleeNode = graph.CreateNode(CpgNodeKind.Method);
        calleeNode.SetProperty("Name", "Execute");

        _ = graph.AddEdge(methodNode.Id, callNode.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(callNode.Id, calleeNode.Id, CpgEdgeKind.Call);

        IReadOnlyList<CpgNode> calls = graph.Methods()
            .Name("Run")
            .Calls()
            .Name("Execute")
            .ToList();
        IReadOnlyList<CpgNode> callees = new Traversal(graph, calls)
            .Callees()
            .ToList();

        Assert.Single(calls);
        Assert.Equal(callNode.Id, calls[0].Id);
        Assert.Single(callees);
        Assert.Equal(calleeNode.Id, callees[0].Id);
    }

    [Fact]
    public void Traversal_ddgInAndDdgOut_followReachingDefEdges()
    {
        CpgGraph graph = new();
        CpgNode sourceNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode sinkNode = graph.CreateNode(CpgNodeKind.Call);
        _ = graph.AddEdge(sourceNode.Id, sinkNode.Id, CpgEdgeKind.ReachingDef, "input");

        IReadOnlyList<CpgNode> forward = new Traversal(graph, new[] { sourceNode })
            .DdgOut()
            .ToList();
        IReadOnlyList<CpgNode> backward = new Traversal(graph, new[] { sinkNode })
            .DdgIn()
            .ToList();

        Assert.Equal(sinkNode.Id, Assert.Single(forward).Id);
        Assert.Equal(sourceNode.Id, Assert.Single(backward).Id);
    }
}
