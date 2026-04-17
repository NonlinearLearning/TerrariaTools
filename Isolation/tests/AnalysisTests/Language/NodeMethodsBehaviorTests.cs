using Analysis.Core;
using Analysis.Language.Nodemethods;
using Analysis.Semantic;
using Xunit;

namespace Analysis.Tests.Language;

public sealed class NodeMethodsBehaviorTests
{
    [Fact]
    public void CallMethods_exposesDispatchReceiverAndArguments()
    {
        CpgGraph graph = new();
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        call.SetProperty("DispatchType", "STATIC_DISPATCH");
        CpgNode receiver = graph.CreateNode(CpgNodeKind.Identifier);
        CpgNode argument = graph.CreateNode(CpgNodeKind.Identifier);
        argument.SetProperty("ArgumentIndex", 1);
        _ = graph.AddEdge(call.Id, receiver.Id, CpgEdgeKind.Receiver);
        _ = graph.AddEdge(call.Id, argument.Id, CpgEdgeKind.Argument);

        Assert.True(CallMethods.IsStatic(call));
        Assert.Equal(receiver.Id, Assert.Single(CallMethods.Receiver(graph, call)).Id);
        Assert.Equal(argument.Id, Assert.Single(CallMethods.Arguments(graph, call, 1)).Id);
    }

    [Fact]
    public void AstNodeMethods_returnsDepthParentChildrenAndStatement()
    {
        CpgGraph graph = new();
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        call.SetProperty("Name", "<operator>.fieldAccess");
        CpgNode identifier = graph.CreateNode(CpgNodeKind.Identifier);
        identifier.SetProperty("Code", "x");
        _ = graph.AddEdge(call.Id, identifier.Id, CpgEdgeKind.Ast);

        Assert.Equal(2, AstNodeMethods.Depth(graph, call));
        Assert.Equal(call.Id, AstNodeMethods.AstParent(graph, identifier)?.Id);
        Assert.Equal(identifier.Id, Assert.Single(AstNodeMethods.AstChildren(graph, call)).Id);
        Assert.Equal(call.Id, AstNodeMethods.Statement(graph, identifier).Id);
    }

    [Fact]
    public void MethodMethods_returnsTopLevelExpressionsAndNumberOfLines()
    {
        CpgGraph graph = new();
        CpgNode method = graph.CreateNode(CpgNodeKind.Method);
        method.SetProperty("Line", 10);
        method.SetProperty("LineEnd", 14);
        CpgNode block = graph.CreateNode(CpgNodeKind.Block);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode local = graph.CreateNode(CpgNodeKind.Local);
        _ = graph.AddEdge(method.Id, block.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(block.Id, call.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(block.Id, local.Id, CpgEdgeKind.Ast);

        Assert.Equal(5, MethodMethods.NumberOfLines(method));
        Assert.Equal(call.Id, Assert.Single(MethodMethods.TopLevelExpressions(graph, method)).Id);
        Assert.Equal(block.Id, MethodMethods.Body(graph, method)?.Id);
    }
}
