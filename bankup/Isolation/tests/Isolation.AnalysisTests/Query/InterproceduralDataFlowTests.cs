using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Language;
using Domain.Analysis.Engine.Query;
using Xunit;

namespace Isolation.AnalysisTests.Query;

public sealed class InterproceduralDataFlowTests
{
    [Fact]
    public void QueryEngine_followsParameterLinksAcrossCalls()
    {
        CpgGraph graph = new();
        CpgNode source = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode calleeParameter = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode sink = graph.CreateNode(CpgNodeKind.Call);
        _ = graph.AddEdge(source.Id, call.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(call.Id, calleeParameter.Id, CpgEdgeKind.ParameterLink, "arg1");
        _ = graph.AddEdge(calleeParameter.Id, sink.Id, CpgEdgeKind.ReachingDef, "input");

        IReadOnlyList<DataFlowPath> paths = new QueryEngine(graph)
            .BackwardFromSinks(new[] { sink.Id }, new[] { source.Id });

        Assert.Equal(new[] { source.Id, call.Id, calleeParameter.Id, sink.Id }, Assert.Single(paths).NodeIds);
    }

    [Fact]
    public void Traversal_argumentsAndStatementsExposeCommonDslSteps()
    {
        CpgGraph graph = new();
        CpgNode method = graph.CreateNode(CpgNodeKind.Method);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        call.SetProperty("Name", "Save");
        CpgNode argument = graph.CreateNode(CpgNodeKind.Identifier);
        argument.SetProperty("Name", "input");
        argument.SetProperty("ArgumentIndex", 1);
        _ = graph.AddEdge(method.Id, call.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(call.Id, argument.Id, CpgEdgeKind.Ast);

        IReadOnlyList<CpgNode> arguments = graph.Calls().Name("Save").Arguments(1).ToList();
        IReadOnlyList<CpgNode> statements = graph.Methods().Statements().ToList();

        Assert.Equal(argument.Id, Assert.Single(arguments).Id);
        Assert.Equal(call.Id, Assert.Single(statements).Id);
    }

    [Fact]
    public void QueryEngine_followsReturnLinksBackToCallSite()
    {
        CpgGraph graph = new();
        CpgNode source = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode calleeReturn = graph.CreateNode(CpgNodeKind.MethodReturn);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode sink = graph.CreateNode(CpgNodeKind.Call);
        _ = graph.AddEdge(source.Id, calleeReturn.Id, CpgEdgeKind.ReachingDef, "value");
        _ = graph.AddEdge(calleeReturn.Id, call.Id, CpgEdgeKind.ParameterLink, "return");
        _ = graph.AddEdge(call.Id, sink.Id, CpgEdgeKind.ReachingDef, "value");

        IReadOnlyList<DataFlowPath> paths = new QueryEngine(graph)
            .BackwardFromSinks(new[] { sink.Id }, new[] { source.Id });

        Assert.Equal(new[] { source.Id, calleeReturn.Id, call.Id, sink.Id }, Assert.Single(paths).NodeIds);
    }
}
