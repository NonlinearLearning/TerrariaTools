using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Query;
using Xunit;

namespace Isolation.AnalysisTests.Query;

public sealed class QueryTaskComponentTests
{
    [Fact]
    public void QueryTaskCreator_createOneTaskPerSink_deduplicatesAndKeepsSinkIds()
    {
        CpgGraph graph = new();
        CpgNode firstSink = graph.CreateNode(CpgNodeKind.Call);
        CpgNode secondSink = graph.CreateNode(CpgNodeKind.Call);

        QueryTaskCreator creator = new(graph);

        IReadOnlyList<QueryTask> tasks = creator.CreateOneTaskPerSink(
            new[] { firstSink.Id, secondSink.Id, firstSink.Id });

        Assert.Equal(new[] { firstSink.Id, secondSink.Id }, tasks.Select(task => task.SinkNodeId));
    }

    [Fact]
    public void QueryTaskSolver_solveBackward_findsPathAcrossReachingDefAndParameterLink()
    {
        CpgGraph graph = new();
        CpgNode source = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        CpgNode calleeParameter = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        CpgNode sink = graph.CreateNode(CpgNodeKind.Call);
        _ = graph.AddEdge(source.Id, call.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(call.Id, calleeParameter.Id, CpgEdgeKind.ParameterLink, "arg1");
        _ = graph.AddEdge(calleeParameter.Id, sink.Id, CpgEdgeKind.ReachingDef, "value");

        QueryTask task = new(sink.Id);
        IReadOnlyList<DataFlowPath> paths = new QueryTaskSolver(graph)
            .SolveBackward(task, new HashSet<long> { source.Id }, maxDepth: 8);

        Assert.Equal(new[] { source.Id, call.Id, calleeParameter.Id, sink.Id }, Assert.Single(paths).NodeIds);
    }

}
