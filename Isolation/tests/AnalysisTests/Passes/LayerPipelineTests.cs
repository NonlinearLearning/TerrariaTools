using Analysis.Core;
using Analysis.Layers;
using Analysis.Semantic;
using Xunit;

namespace Analysis.Tests.Passes;

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
