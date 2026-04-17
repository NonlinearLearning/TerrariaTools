using Analysis.Core;
using Analysis.Passes.DataFlow;
using Analysis.Query;
using Analysis.Semantic.Flows;
using Xunit;

namespace Analysis.Tests.Passes;

public sealed class SemanticDataFlowPassTests
{
    [Fact]
    public void SemanticDataFlowPass_appliesArgumentToReturnRulesAtCallSite()
    {
        CpgGraph graph = new();
        CpgNode source = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        source.SetProperty("Name", "input");
        CpgNode argument = graph.CreateNode(CpgNodeKind.Identifier);
        argument.SetProperty("Name", "input");
        argument.SetProperty("ArgumentIndex", 1);
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);
        call.SetProperty("Name", "Clean");
        call.SetProperty("MethodFullName", "Demo.Sanitizer.Clean(string)");
        CpgNode sink = graph.CreateNode(CpgNodeKind.Call);
        sink.SetProperty("Name", "Sink");
        _ = graph.AddEdge(source.Id, argument.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(call.Id, argument.Id, CpgEdgeKind.Argument);
        _ = graph.AddEdge(call.Id, sink.Id, CpgEdgeKind.ReachingDef, "cleaned");

        FullNameSemantics semantics = FullNameSemantics.FromRules(
            new[]
            {
                new MethodFlowRule(
                    "Demo.Sanitizer.Clean(string)",
                    new FlowEndpoint(FlowEndpointKind.Argument, 1),
                    new FlowEndpoint(FlowEndpointKind.Return)),
            });

        new BuildSemanticDataFlowPass(semantics).Run(graph);

        IReadOnlyList<DataFlowPath> paths = new DataFlowQuery(graph)
            .From(source.Id)
            .To(sink.Id)
            .FindPaths();

        Assert.Equal(new[] { source.Id, argument.Id, call.Id, sink.Id }, Assert.Single(paths).NodeIds);
    }
}
