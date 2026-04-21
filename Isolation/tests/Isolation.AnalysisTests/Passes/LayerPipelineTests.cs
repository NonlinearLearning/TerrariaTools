using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Layers;
using Domain.Analysis.Engine.Semantic;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class LayerPipelineTests
{
    [Fact]
    public void LayerPipeline_appliesDependenciesBeforeRequestedLayer()
    {
        CpgGraph graph = new();
        _ = graph.CreateNode(CpgNodeKind.MetaData);
        LayerPipeline pipeline = new(new ILayerCreator[] { new CallGraphLayer(), new TypeRelationsLayer(), new BaseLayer() });

        pipeline.Apply(graph, CallGraphLayer.OverlayNameValue);

        Assert.Equal(
            new[] { BaseLayer.OverlayNameValue, TypeRelationsLayer.OverlayNameValue, CallGraphLayer.OverlayNameValue },
            Overlays.AppliedOverlays(graph));
    }

    [Fact]
    public void X2CpgLayers_exposeJoernOverlayNamesAndPassNames()
    {
        Assert.Contains("BuildStaticCallGraphPass", new CallGraphLayer().PassNames());
        Assert.Contains("BuildCfgPass", new ControlFlowLayer().PassNames());
        Assert.Contains("BuildTypeHierarchyPass", new TypeRelationsLayer().PassNames());
    }
}
