using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Query;
using Xunit;

namespace Isolation.AnalysisTests.Query;

public sealed class CpgQueryTests
{
    [Fact]
    public void Query_nodesByKind_returnsOnlyMatchingNodes()
    {
        CpgGraph graph = new();
        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        CpgNode identifierNode = graph.CreateNode(CpgNodeKind.Identifier);

        CpgQuery query = new(graph);

        IReadOnlyList<CpgNode> result = query.Nodes()
            .OfKind(CpgNodeKind.Call)
            .ToList();

        Assert.Single(result);
        Assert.Equal(callNode.Id, result[0].Id);
        Assert.DoesNotContain(result, node => node.Id == methodNode.Id);
        Assert.DoesNotContain(result, node => node.Id == identifierNode.Id);
    }

    [Fact]
    public void Query_outgoing_returnsTargetsReachedBySpecifiedEdgeKind()
    {
        CpgGraph graph = new();
        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        _ = graph.AddEdge(methodNode.Id, callNode.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(methodNode.Id, localNode.Id, CpgEdgeKind.Ref);

        CpgQuery query = new(graph);

        IReadOnlyList<CpgNode> result = query.Nodes()
            .WhereId(methodNode.Id)
            .Outgoing(CpgEdgeKind.Ast)
            .ToList();

        Assert.Single(result);
        Assert.Equal(callNode.Id, result[0].Id);
    }

    [Fact]
    public void DataFlowQuery_reachableByReachingDef_findsDefinitionPath()
    {
        CpgGraph graph = new();
        CpgNode parameterNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        parameterNode.SetProperty("Name", "input");
        CpgNode assignmentNode = graph.CreateNode(CpgNodeKind.Call);
        assignmentNode.SetProperty("Name", "=");
        CpgNode returnNode = graph.CreateNode(CpgNodeKind.ControlStructure);
        returnNode.SetProperty("ControlStructureType", "RETURN");

        _ = graph.AddEdge(parameterNode.Id, assignmentNode.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(assignmentNode.Id, returnNode.Id, CpgEdgeKind.ReachingDef, "value");

        CpgQuery query = new(graph);

        IReadOnlyList<DataFlowPath> paths = query.DataFlow()
            .From(parameterNode.Id)
            .To(returnNode.Id)
            .FindPaths();

        DataFlowPath path = Assert.Single(paths);
        Assert.Equal(new[] { parameterNode.Id, assignmentNode.Id, returnNode.Id }, path.NodeIds);
    }

    [Fact]
    public void QueryEngine_backwardFromSinks_findsPathsFromSourcesToSinks()
    {
        CpgGraph graph = new();
        CpgNode sourceNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode middleNode = graph.CreateNode(CpgNodeKind.Call);
        CpgNode sinkNode = graph.CreateNode(CpgNodeKind.Call);

        _ = graph.AddEdge(sourceNode.Id, middleNode.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(middleNode.Id, sinkNode.Id, CpgEdgeKind.ReachingDef, "sql");

        QueryEngine engine = new(graph);

        IReadOnlyList<DataFlowPath> paths = engine.BackwardFromSinks(
            new[] { sinkNode.Id },
            new[] { sourceNode.Id });

        DataFlowPath path = Assert.Single(paths);
        Assert.Equal(new[] { sourceNode.Id, middleNode.Id, sinkNode.Id }, path.NodeIds);
    }
}
