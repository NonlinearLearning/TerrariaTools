using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Slicing;
using Xunit;

namespace Isolation.AnalysisTests.Slicing;

public sealed class DataFlowSlicerTests
{
    [Fact]
    public void SliceBackward_returnsNodesThatReachCriterion()
    {
        CpgGraph graph = new();
        CpgNode parameterNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode assignmentNode = graph.CreateNode(CpgNodeKind.Call);
        CpgNode returnNode = graph.CreateNode(CpgNodeKind.ControlStructure);

        _ = graph.AddEdge(parameterNode.Id, assignmentNode.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(assignmentNode.Id, returnNode.Id, CpgEdgeKind.ReachingDef, "value");

        DataFlowSlicer slicer = new(graph);

        SliceResult result = slicer.Slice(new SliceCriterion(returnNode.Id), SliceDirection.Backward);

        Assert.Equal(returnNode.Id, result.CriterionNodeId);
        Assert.Equal(
            new[] { parameterNode.Id, assignmentNode.Id, returnNode.Id },
            result.Nodes.Select(node => node.Id).OrderBy(id => id).ToArray());
        Assert.Equal(2, result.Edges.Count);
    }

    [Fact]
    public void SliceForward_returnsNodesReachedFromCriterion()
    {
        CpgGraph graph = new();
        CpgNode sourceNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        CpgNode sinkNode = graph.CreateNode(CpgNodeKind.Call);

        _ = graph.AddEdge(sourceNode.Id, callNode.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(callNode.Id, sinkNode.Id, CpgEdgeKind.ReachingDef, "sql");

        DataFlowSlicer slicer = new(graph);

        SliceResult result = slicer.Slice(new SliceCriterion(sourceNode.Id), SliceDirection.Forward);

        Assert.Equal(
            new[] { sourceNode.Id, callNode.Id, sinkNode.Id },
            result.Nodes.Select(node => node.Id).OrderBy(id => id).ToArray());
        Assert.Equal(2, result.Edges.Count);
    }

    [Fact]
    public void DataFlowSlicing_calculate_returnsSerializableSliceFromSink()
    {
        CpgGraph graph = new();
        CpgNode sourceNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        sourceNode.SetProperty("Name", "input");
        CpgNode sinkNode = graph.CreateNode(CpgNodeKind.Call);
        sinkNode.SetProperty("Name", "Execute");
        _ = graph.AddEdge(sourceNode.Id, sinkNode.Id, CpgEdgeKind.ReachingDef, "input");

        DataFlowSlicing slicing = new(graph);

        DataFlowSlice slice = slicing.Calculate(new[] { sinkNode.Id });

        Assert.Equal(new[] { sourceNode.Id, sinkNode.Id }, slice.Nodes.Select(node => node.Id).OrderBy(id => id));
        SliceEdge edge = Assert.Single(slice.Edges);
        Assert.Equal(sourceNode.Id, edge.SourceId);
        Assert.Equal(sinkNode.Id, edge.TargetId);
    }

    [Fact]
    public void UsageSlicing_calculateForDeclaration_returnsReferencesAndParentCall()
    {
        CpgGraph graph = new();
        CpgNode localNode = graph.CreateNode(CpgNodeKind.Local);
        localNode.SetProperty("Name", "value");
        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", "Use");
        CpgNode identifierNode = graph.CreateNode(CpgNodeKind.Identifier);
        identifierNode.SetProperty("Name", "value");
        identifierNode.SetProperty("AstParentId", callNode.Id);
        _ = graph.AddEdge(identifierNode.Id, localNode.Id, CpgEdgeKind.Ref);

        UsageSlicing slicing = new(graph);

        DataFlowSlice slice = slicing.CalculateForDeclaration(localNode.Id);

        Assert.Contains(slice.Nodes, node => node.Id == localNode.Id);
        Assert.Contains(slice.Nodes, node => node.Id == identifierNode.Id);
        Assert.Contains(slice.Nodes, node => node.Id == callNode.Id);
    }
}
