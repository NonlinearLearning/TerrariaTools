using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Query;
using Xunit;

namespace Isolation.AnalysisTests.Query;

public sealed class QueryEngineDetailTests
{
    [Fact]
    public void AccessPathUsage_extractsMemberPathFromNestedAst()
    {
        CpgGraph graph = new();
        CpgNode identifier = graph.CreateNode(CpgNodeKind.Identifier);
        identifier.SetProperty("Name", "user");
        CpgNode field = graph.CreateNode(CpgNodeKind.Member);
        field.SetProperty("Name", "Profile");
        CpgNode nested = graph.CreateNode(CpgNodeKind.Member);
        nested.SetProperty("Name", "Email");
        _ = graph.AddEdge(identifier.Id, field.Id, CpgEdgeKind.Ast);
        _ = graph.AddEdge(field.Id, nested.Id, CpgEdgeKind.Ast);

        AccessPathUsage usage = AccessPathUsage.FromNode(graph, nested);

        Assert.Equal(identifier.Id, usage.BaseNodeId);
        Assert.Equal(new[] { "Profile", "Email" }, usage.AccessPath);
    }

    [Fact]
    public void HeldTaskCompletion_resolvesPreviouslyHeldTasksWhenResultArrives()
    {
        QueryTask task = new(42);
        DataFlowPath path = new(new long[] { 1, 42 });
        HeldTaskCompletion completion = new();

        completion.Hold(task);
        completion.AddResult(task, path);

        IReadOnlyList<DataFlowPath> completed = completion.CompleteHeldTasks();
        Assert.Equal(path.NodeIds, Assert.Single(completed).NodeIds);
        Assert.Empty(completion.HeldTasks);
    }
}
