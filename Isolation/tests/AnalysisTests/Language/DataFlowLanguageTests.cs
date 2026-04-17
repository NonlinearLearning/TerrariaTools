using Analysis.Core;
using Analysis.Language;
using Analysis.Language.DataFlow;
using Xunit;
using DataFlowDisplayPath = Analysis.Language.DataFlow.Path;

namespace Analysis.Tests.Language;

public sealed class DataFlowLanguageTests
{
    [Fact]
    public void ExtendedCfgNode_reachableByFlowsReturnsVisiblePaths()
    {
        CpgGraph graph = new();
        CpgNode source = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        source.SetProperty("Name", "input");
        CpgNode sink = graph.CreateNode(CpgNodeKind.Call);
        sink.SetProperty("Name", "Save");
        _ = graph.AddEdge(source.Id, sink.Id, CpgEdgeKind.ReachingDef, "input");

        IReadOnlyList<DataFlowDisplayPath> paths = new Traversal(graph, new[] { sink })
            .AsExtendedCfgNode()
            .ReachableByFlows(new Traversal(graph, new[] { source }));

        DataFlowDisplayPath path = Assert.Single(paths);
        Assert.Equal(new[] { source.Id, sink.Id }, path.Elements.Select(node => node.Id));
        Assert.Contains("input", path.ResultPairs().Select(pair => pair.Code));
    }
}
