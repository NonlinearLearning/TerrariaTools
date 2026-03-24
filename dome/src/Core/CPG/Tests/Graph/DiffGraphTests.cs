namespace TerrariaTools.Dome.Core.Cpg.Tests.Graph;

public sealed class DiffGraphTests
{
    [Fact]
    public void DiffGraphApply_ShouldAppendNodesAndEdgesToCpg()
    {
        DomeCpg cpg = new();
        DiffGraph diff = new();

        MethodNode source = new("method-1");
        CallNode target = new("call-1");

        diff.AddNode(source);
        diff.AddNode(target);
        diff.AddEdge(new CpgEdge("AST", source.Id, target.Id));

        DiffGraphApplier.Apply(cpg, diff);

        Assert.Contains(cpg.Nodes, node => node.Id == "method-1");
        Assert.Contains(cpg.Nodes, node => node.Id == "call-1");
        Assert.Contains(cpg.Edges, edge => edge.Label == "AST" && edge.SourceId == "method-1" && edge.TargetId == "call-1");
    }

    [Fact]
    public void DomeCpg_ShouldIndexNodesAndEdgesByIdKindAndLabel()
    {
        DomeCpg cpg = new();
        DiffGraph diff = new();

        MetaDataNode metaDataNode = new("meta-data", "CSHARP", "input.cs", "0.1");
        MethodNode methodNode = new("method:N.C.M", "M", "N.C", "void", "N.C.M", "void()", "void");
        CallNode callNode = new("call:N.C.M:1", "N.C.M", "A", 1, "N.C", "void", null, "N.C.A");

        diff.AddNode(metaDataNode);
        diff.AddNode(methodNode);
        diff.AddNode(callNode);
        diff.AddEdge(new CpgEdge(EdgeKinds.Ast, methodNode.Id, callNode.Id));
        diff.AddEdge(new CpgEdge(EdgeKinds.Call, methodNode.Id, "method:N.C.A"));

        DiffGraphApplier.Apply(cpg, diff);

        Assert.True(cpg.ContainsNode(methodNode.Id));
        Assert.Same(methodNode, cpg.FindNodeById<MethodNode>(methodNode.Id));
        Assert.Equal(metaDataNode, cpg.MetaData);
        Assert.Contains(methodNode, cpg.GetNodesByKind(NodeKinds.Method));
        Assert.Contains(callNode, cpg.GetNodesByKind(NodeKinds.Call));
        Assert.Contains(cpg.GetEdgesByLabel(EdgeKinds.Ast), edge => edge.SourceId == methodNode.Id && edge.TargetId == callNode.Id);
        Assert.Contains(cpg.GetEdgesByLabel(EdgeKinds.Call), edge => edge.SourceId == methodNode.Id && edge.TargetId == "method:N.C.A");
    }

    [Fact]
    public void DomeCpg_ShouldExposeTypedNodeAndDirectionalEdgeIndexes()
    {
        DomeCpg cpg = new();
        DiffGraph diff = new();

        MethodNode methodNode = new("method:N.C.M", "M", "N.C", "void", "N.C.M", "void()", "void");
        MethodRefNode methodRefNode = new("method-ref:N.C.M:Use:0:0", "A", "System.Action");
        CallNode callNode = new("call:N.C.M:Use:0", "M", "Use", 0, "N.C", "void", null, "N.C.Use");
        MethodNode targetMethodNode = new("method:N.C.A", "A", "N.C", "void", "N.C.A", "void()", "void");

        diff.AddNode(methodNode);
        diff.AddNode(methodRefNode);
        diff.AddNode(callNode);
        diff.AddNode(targetMethodNode);
        diff.AddEdge(new CpgEdge(EdgeKinds.Argument, callNode.Id, methodRefNode.Id));
        diff.AddEdge(new CpgEdge(EdgeKinds.Ref, methodRefNode.Id, targetMethodNode.Id));

        DiffGraphApplier.Apply(cpg, diff);

        Assert.Contains(methodRefNode, cpg.GetNodesByKind<MethodRefNode>(NodeKinds.MethodRef));
        Assert.Contains(callNode, cpg.GetNodesByKind<CallNode>(NodeKinds.Call));
        Assert.Contains(
            cpg.GetOutgoingEdges(EdgeKinds.Argument, callNode.Id),
            edge => edge.TargetId == methodRefNode.Id);
        Assert.Contains(
            cpg.GetIncomingEdges(EdgeKinds.Argument, methodRefNode.Id),
            edge => edge.SourceId == callNode.Id);
        Assert.Contains(
            cpg.GetOutgoingEdges(EdgeKinds.Ref, methodRefNode.Id),
            edge => edge.TargetId == targetMethodNode.Id);
    }
}
