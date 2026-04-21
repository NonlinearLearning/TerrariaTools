using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Query;
using Logic.Analysis.Engine.Language;
using Logic.Analysis.Engine.Language.DataFlow;
using Xunit;
using DataFlowDisplayPath = Logic.Analysis.Engine.Language.DataFlow.Path;

namespace Isolation.AnalysisTests.Language;

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

    [Fact]
    public void ExtendedCfgNode_reachableByDetailedAndReachableBy_deduplicateSources()
    {
        CpgGraph graph = new();
        CpgNode source = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        source.SetProperty("Name", "input");
        CpgNode sinkA = graph.CreateNode(CpgNodeKind.Call);
        sinkA.SetProperty("Name", "SaveA");
        CpgNode sinkB = graph.CreateNode(CpgNodeKind.Call);
        sinkB.SetProperty("Name", "SaveB");
        _ = graph.AddEdge(source.Id, sinkA.Id, CpgEdgeKind.ReachingDef, "input");
        _ = graph.AddEdge(source.Id, sinkB.Id, CpgEdgeKind.ReachingDef, "input");

        ExtendedCfgNode extendedCfgNode = new Traversal(graph, new[] { sinkA, sinkB })
            .AsExtendedCfgNode();
        Traversal sourceTraversal = new(graph, new[] { source, source });

        IReadOnlyList<CpgNode> reachableSources = extendedCfgNode.ReachableBy(sourceTraversal);
        IReadOnlyList<DataFlowPath> detailedPaths = extendedCfgNode.ReachableByDetailed(sourceTraversal);

        Assert.Equal(source.Id, Assert.Single(reachableSources).Id);
        Assert.Equal(2, detailedPaths.Count);
        Assert.Contains(detailedPaths, path => path.NodeIds.SequenceEqual(new[] { source.Id, sinkA.Id }));
        Assert.Contains(detailedPaths, path => path.NodeIds.SequenceEqual(new[] { source.Id, sinkB.Id }));
    }
}
