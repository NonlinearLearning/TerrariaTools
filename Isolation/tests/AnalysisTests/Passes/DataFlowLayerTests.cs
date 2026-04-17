using Analysis.Core;
using Analysis.Layers.DataFlow;
using Analysis.Semantic;
using Xunit;

namespace Analysis.Tests.Passes;

public sealed class DataFlowLayerTests
{
    [Fact]
    public void OssDataFlowLayer_appliesDataflowOverlay()
    {
        CpgGraph graph = new();
        _ = graph.CreateNode(CpgNodeKind.MetaData);

        new OssDataFlowLayer().Apply(graph);

        Assert.Contains(OssDataFlowLayer.OverlayNameValue, Overlays.AppliedOverlays(graph));
    }

}
