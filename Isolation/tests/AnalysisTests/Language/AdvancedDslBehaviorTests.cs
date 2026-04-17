using Analysis.Core;
using Analysis.Language;
using Analysis.Language.Nodemethods;
using Xunit;

namespace Analysis.Tests.Language;

public sealed class AdvancedDslBehaviorTests
{
    [Fact]
    public void Traversal_chainsAstCallGraphCfgAndDataFlowSteps()
    {
        CpgGraph graph = new();
        CpgNode method = graph.CreateNode(CpgNodeKind.Method);
        method.SetProperty("Name", "Run");
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        call.SetProperty("Name", "Save");
        CpgNode callee = graph.CreateNode(CpgNodeKind.Method);
        callee.SetProperty("Name", "Save");
        CpgNode next = graph.CreateNode(CpgNodeKind.Call);
        next.SetProperty("Name", "After");
        CpgNode source = graph.CreateNode(CpgNodeKind.Identifier);
        source.SetProperty("Name", "input");
        CpgNode sink = graph.CreateNode(CpgNodeKind.Call);
        sink.SetProperty("Name", "Sink");
        _ = graph.AddEdge(method.Id, call.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(call.Id, callee.Id, CpgEdgeKind.Call);
        _ = graph.AddEdge(call.Id, next.Id, CpgEdgeKind.Cfg);
        _ = graph.AddEdge(source.Id, call.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(call.Id, sink.Id, CpgEdgeKind.ParameterLink, "return");

        Assert.Equal(callee.Id, graph.Calls().Name("Save").Callees().FirstOrDefault()?.Id);
        Assert.Equal(next.Id, graph.Calls().Name("Save").CfgNext().FirstOrDefault()?.Id);
        Assert.Equal(source.Id, graph.Calls().Name("Save").DdgIn().FirstOrDefault()?.Id);
        Assert.Equal(sink.Id, graph.Calls().Name("Save").DdgOut().FirstOrDefault()?.Id);
        Assert.Equal(call.Id, graph.Methods().Name("Run").AstDescendants().OfKind(CpgNodeKind.Call).FirstOrDefault()?.Id);
    }

    [Fact]
    public void CallMethods_ordersArgumentsAndRemovesDuplicatesAcrossAstAndArgumentEdges()
    {
        CpgGraph graph = new();
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode second = graph.CreateNode(CpgNodeKind.Identifier);
        second.SetProperty("ArgumentIndex", 2);
        CpgNode first = graph.CreateNode(CpgNodeKind.Identifier);
        first.SetProperty("ArgumentIndex", 1);
        _ = graph.AddEdge(call.Id, second.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(call.Id, first.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(call.Id, first.Id, CpgEdgeKind.Argument);
        _ = graph.AddEdge(call.Id, second.Id, CpgEdgeKind.Argument);

        IReadOnlyList<CpgNode> arguments = CallMethods.Arguments(graph, call);

        Assert.Equal(new[] { first.Id, second.Id }, arguments.Select(node => node.Id));
    }
}
