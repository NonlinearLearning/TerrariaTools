using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Semantic;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class AstModelTests
{
    [Fact]
    public void Ast_withChildStoresAstEdgesAndAssignsOrder()
    {
        CpgGraph graph = new();
        CpgNode parent = graph.CreateNode(CpgNodeKind.Call);
        CpgNode firstChild = graph.CreateNode(CpgNodeKind.Identifier);
        CpgNode secondChild = graph.CreateNode(CpgNodeKind.Literal);

        Ast ast = Ast.FromRoot(parent)
            .WithChildren(new[] { Ast.FromRoot(firstChild), Ast.FromRoot(secondChild) });
        ast.StoreInGraph(graph);

        Assert.Equal(1, parent.Property<int>("Order"));
        Assert.Equal(1, firstChild.Property<int>("Order"));
        Assert.Equal(2, secondChild.Property<int>("Order"));
        Assert.Equal(new[] { firstChild.Id, secondChild.Id }, graph.GetOutgoingEdges(parent.Id, CpgEdgeKind.Ast).Select(edge => edge.TargetId));
    }

    [Fact]
    public void AstNodeBuilder_createsNodesWithCommonProperties()
    {
        CpgGraph graph = new();
        TestAstNodeBuilder builder = new(graph);

        CpgNode call = builder.CallNode("Save(input)", "Save", "Demo.Save", "STATIC_DISPATCH", "void", 7, 3);

        Assert.Equal(CpgNodeKind.Call, call.Kind);
        Assert.Equal("Save", call.Property<string>("Name"));
        Assert.Equal("Demo.Save", call.Property<string>("MethodFullName"));
        Assert.Equal(7, call.Property<int>("Line"));
        Assert.Equal(3, call.Property<int>("Column"));
    }

    [Fact]
    public void Ast_supportsArgumentReceiverAndConditionEdges()
    {
        CpgGraph graph = new();
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode receiver = graph.CreateNode(CpgNodeKind.Identifier);
        CpgNode argument = graph.CreateNode(CpgNodeKind.Identifier);
        CpgNode condition = graph.CreateNode(CpgNodeKind.Identifier);

        Ast.FromRoot(call)
            .WithChild(Ast.FromRoot(receiver))
            .WithChild(Ast.FromRoot(argument))
            .WithReceiverEdge(call, receiver)
            .WithArgEdges(call, new[] { argument }, 1)
            .WithConditionEdge(call, condition)
            .Merge(Ast.FromRoot(condition))
            .StoreInGraph(graph);

        Assert.Equal(1, argument.Property<int>("ArgumentIndex"));
        Assert.Contains(graph.GetOutgoingEdges(call.Id, CpgEdgeKind.Receiver), edge => edge.TargetId == receiver.Id);
        Assert.Contains(graph.GetOutgoingEdges(call.Id, CpgEdgeKind.Argument), edge => edge.TargetId == argument.Id);
        Assert.Contains(graph.GetOutgoingEdges(call.Id, CpgEdgeKind.Condition), edge => edge.TargetId == condition.Id);
    }

    private sealed class TestAstNodeBuilder : AstNodeBuilder
    {
        public TestAstNodeBuilder(CpgGraph graph)
            : base(graph)
        {
        }
    }
}
